#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Asynchronous Flask API for WorldGen (text/image -> 3D), designed to work behind Cloudflare Tunnel.

Flow:
1) POST /jobs             -> create job, immediately returns {"job_id": "...", "status_url": "...", "download_url": "..."}
2) GET  /jobs/<job_id>    -> check status {"status": "queued|running|done|error", ...}
3) GET  /jobs/<job_id>/download -> stream GLB when done
4) DELETE /jobs/<job_id>  -> optional cleanup

Notes:
- Outputs to /mnt/d/worldgen_outputs/<job_id>/ by default; falls back to ./worldgen_outputs
- i2s resizes long side to MAX_SIDE (default 768) for speed/VRAM
- All uploaded images are converted to RGB
"""

import os
import io
import gc
import uuid
import time
import json
import queue
import threading
from dataclasses import dataclass, field
from typing import Optional
import shutil, numpy as np, trimesh
from flask import Flask, request, jsonify, send_file, url_for
from PIL import Image

# ---------- Optional: HF login via env ----------
try:
    from huggingface_hub import login as hf_login
    _tok = os.environ.get("HUGGINGFACE_HUB_TOKEN")
    if _tok:
        hf_login(token=_tok)
except Exception:
    pass

# ---------- Torch / device ----------
import torch
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
os.environ.setdefault("CUDA_VISIBLE_DEVICES", "0")
# 移除會在某些 Torch 版本報錯的 allocator 設定
# os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "max_split_size_mb=64")

# ---------- WorldGen ----------
from worldgen import WorldGen
WG_T2S = None
WG_I2S = None

def get_t2s(low_vram=True):
    global WG_T2S
    if WG_T2S is None:
        WG_T2S = WorldGen(mode="t2s", device=DEVICE, low_vram=low_vram)
    return WG_T2S

def get_i2s(low_vram=True):
    global WG_I2S
    if WG_I2S is None:
        WG_I2S = WorldGen(mode="i2s", device=DEVICE, low_vram=low_vram)
    return WG_I2S

# ---------- Output root ----------
def _choose_outdir():
    for p in ("/mnt/d/worldgen_outputs", "./worldgen_outputs"):
        try:
            os.makedirs(p, exist_ok=True)
            return os.path.abspath(p)
        except Exception:
            continue
    return os.path.abspath("./")
OUTPUT_ROOT = _choose_outdir()

MAX_SIDE = int(os.environ.get("WG_MAX_SIDE", "768"))  # image max side for i2s

# ---------- Export helpers (trimesh-based, with debug) ----------
import shutil
import numpy as np
import trimesh

def _coerce_to_trimesh(obj):
    """
    盡量把各種 mesh 轉成 trimesh.Trimesh：
    - Trimesh 直接回傳
    - Open3D TriangleMesh（含 CUDA 版）→ 取 vertices / triangles
    - 具 .to_trimesh() / .tri_mesh
    - 具 vertices & faces 或 dict{'vertices','faces'}
    - list/tuple → 取第一個
    """
    # 1) 已是 Trimesh
    if isinstance(obj, trimesh.Trimesh):
        return obj

    # 2) Open3D TriangleMesh（legacy 或 CUDA/t）
    try:
        import open3d as o3d  # 若沒安裝會丟例外，直接跳過
        # 型別判斷：類名或 module 前綴帶 open3d
        mod = type(obj).__module__
        if mod and mod.startswith("open3d"):
            # 轉 legacy（CUDA -> CPU）
            mesh_legacy = obj.to_legacy() if hasattr(obj, "to_legacy") else obj
            if hasattr(mesh_legacy, "vertices") and hasattr(mesh_legacy, "triangles"):
                V = np.asarray(mesh_legacy.vertices)
                F = np.asarray(mesh_legacy.triangles)
                return trimesh.Trimesh(vertices=V, faces=F, process=False)
    except Exception:
        pass

    # 3) 其他轉換途徑
    if hasattr(obj, "to_trimesh"):
        try:
            tm = obj.to_trimesh()
            if isinstance(tm, trimesh.Trimesh):
                return tm
        except Exception:
            pass

    if hasattr(obj, "tri_mesh"):
        tm = getattr(obj, "tri_mesh")
        if isinstance(tm, trimesh.Trimesh):
            return tm

    # 有 vertices & faces
    if hasattr(obj, "vertices") and hasattr(obj, "faces"):
        try:
            V = np.asarray(obj.vertices)
            F = np.asarray(obj.faces)
            return trimesh.Trimesh(vertices=V, faces=F, process=False)
        except Exception:
            pass

    # dict 格式
    if isinstance(obj, dict):
        if "trimesh" in obj and isinstance(obj["trimesh"], trimesh.Trimesh):
            return obj["trimesh"]
        if "vertices" in obj and "faces" in obj:
            try:
                V = np.asarray(obj["vertices"]); F = np.asarray(obj["faces"])
                return trimesh.Trimesh(vertices=V, faces=F, process=False)
            except Exception:
                pass
        # 也容忍 'triangles' 當 faces
        if "vertices" in obj and "triangles" in obj:
            try:
                V = np.asarray(obj["vertices"]); F = np.asarray(obj["triangles"])
                return trimesh.Trimesh(vertices=V, faces=F, process=False)
            except Exception:
                pass

    if isinstance(obj, (list, tuple)) and len(obj) > 0:
        return _coerce_to_trimesh(obj[0])

    return None


def _export_mesh_to_glb(mesh_obj, out_path: str):
    """
    能盡量把各種 mesh 輸出成 GLB：
      - 檔案路徑（.glb/.gltf/.ply/.obj/...）
      - 物件有 .export() / .save()
      - Open3D TriangleMesh（直接用 o3d 或轉成 Trimesh）
      - 任何可被 _coerce_to_trimesh() 處理的物件
    """
    # Debug
    tname = type(mesh_obj).__name__
    attrs = [a for a in dir(mesh_obj) if not a.startswith("_")]
    print(f"[DEBUG] _export_mesh_to_glb: type={tname}, first_attrs={attrs[:15]}")

    # (1) 若是檔案路徑
    if isinstance(mesh_obj, str) and os.path.isfile(mesh_obj):
        src = mesh_obj
        ext = os.path.splitext(src)[1].lower()
        if ext in [".glb", ".gltf"]:
            shutil.copy2(src, out_path); print(f"[DEBUG] Copied {src} -> {out_path}"); return
        try:
            tm = trimesh.load(src, force="mesh", skip_materials=True)
            tm.export(out_path); print(f"[DEBUG] trimesh exported -> {out_path}"); return
        except Exception as e:
            raise RuntimeError(f"Cannot convert file '{src}' to GLB: {e}")

    # (2) 有 export / save
    if hasattr(mesh_obj, "export"):
        try:
            mesh_obj.export(out_path if out_path.lower().endswith(".glb") else out_path, file_type="glb")
            print(f"[DEBUG] Used .export() -> {out_path}"); return
        except Exception as e:
            print(f"[DEBUG] .export() failed: {e}")
    if hasattr(mesh_obj, "save"):
        try:
            mesh_obj.save(out_path); print(f"[DEBUG] Used .save() -> {out_path}"); return
        except Exception as e:
            print(f"[DEBUG] .save() failed: {e}")

    # (3) Open3D：嘗試直接用 o3d 輸出（若版本支援 glb/gltf）
    try:
        import open3d as o3d
        if type(mesh_obj).__module__.startswith("open3d"):
            m = mesh_obj.to_legacy() if hasattr(mesh_obj, "to_legacy") else mesh_obj
            try:
                # 許多 Open3D 版本支援 .glb/.gltf 寫出
                ok = o3d.io.write_triangle_mesh(out_path, m, write_triangle_uvs=True)
                if ok:
                    print(f"[DEBUG] open3d.io.write_triangle_mesh -> {out_path}"); return
            except Exception as e:
                print(f"[DEBUG] open3d write_triangle_mesh failed: {e}")
            # 改走 Trimesh
            V = np.asarray(m.vertices); F = np.asarray(m.triangles)
            trimesh.Trimesh(vertices=V, faces=F, process=False).export(out_path)
            print(f"[DEBUG] Open3D→Trimesh exported -> {out_path}"); return
    except Exception:
        pass

    # (4) 一般 coercion
    tm = _coerce_to_trimesh(mesh_obj)
    if tm is not None:
        tm.export(out_path); print(f"[DEBUG] Coerced to Trimesh -> {out_path}"); return

    # (5) 仍失敗
    raise RuntimeError(f"Unknown mesh object type '{tname}' (no usable export). attrs={attrs[:20]}...")

# ---------- Jobs ----------
@dataclass
class Job:
    job_id: str
    mode: str                      # "t2s" or "i2s"
    prompt: Optional[str] = None   # for t2s
    image_bytes: Optional[bytes] = None
    image_url: Optional[str] = None
    status: str = "queued"         # queued|running|done|error
    message: str = ""
    out_dir: str = ""
    out_glb: Optional[str] = None
    created_at: float = field(default_factory=time.time)

JOB_QUEUE: "queue.Queue[Job]" = queue.Queue()
JOBS: dict[str, Job] = {}

# ---------- Worker ----------
def _resize_for_i2s(pil_img: Image.Image, max_side: int) -> Image.Image:
    W, H = pil_img.size
    scale = max_side / max(W, H)
    if scale < 1.0:
        pil_img = pil_img.resize((int(W*scale), int(H*scale)), Image.LANCZOS)
    return pil_img

def _worker():
    while True:
        job: Job = JOB_QUEUE.get()
        if job is None:
            break
        try:
            job.status = "running"
            job.out_dir = os.path.join(OUTPUT_ROOT, job.job_id)
            os.makedirs(job.out_dir, exist_ok=True)
            out_glb = os.path.join(job.out_dir, "scene.glb")

            if job.mode == "t2s":
                wg = get_t2s(low_vram=True)
                with torch.inference_mode():
                    mesh = wg.generate_world(job.prompt, return_mesh=True)

            elif job.mode == "i2s":
                # convert to RGB always
                if job.image_bytes:
                    img = Image.open(io.BytesIO(job.image_bytes)).convert("RGB")
                elif job.image_url:
                    import urllib.request
                    with urllib.request.urlopen(job.image_url) as r:
                        img = Image.open(io.BytesIO(r.read())).convert("RGB")
                else:
                    raise ValueError("No image provided for i2s.")

                img = _resize_for_i2s(img, MAX_SIDE)

                wg = get_i2s(low_vram=True)
                with torch.inference_mode():
                    try:
                        mesh = wg.generate_world(image=img, return_mesh=True)
                    except TypeError:
                        mesh = wg.generate_world(img, return_mesh=True)
            else:
                raise ValueError("mode must be 't2s' or 'i2s'")

            print(f"[worker] mesh type={type(mesh)}")
            _export_mesh_to_glb(mesh, out_glb)
            job.out_glb = out_glb
            job.status = "done"
            job.message = "ok"

        except Exception as e:
            job.status = "error"
            job.message = str(e)

        finally:
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            JOB_QUEUE.task_done()

worker_thread = threading.Thread(target=_worker, daemon=True)
worker_thread.start()

# ---------- Flask App ----------
app = Flask(__name__)

# ✅ Trust Cloudflare/Proxy headers so url_for respects external scheme/host
from werkzeug.middleware.proxy_fix import ProxyFix
app.wsgi_app = ProxyFix(app.wsgi_app, x_proto=1, x_host=1)
app.config['PREFERRED_URL_SCHEME'] = 'https'

@app.get("/health")
def health():
    return jsonify({
        "ok": True,
        "device": DEVICE,
        "output_root": OUTPUT_ROOT,
        "jobs": len(JOBS),
        "queue": JOB_QUEUE.qsize(),
        "max_side": MAX_SIDE,
    })

@app.post("/jobs")
def create_job():
    """
    Create a new job.
    Form-data or JSON:
      - mode: "t2s" or "i2s" (default i2s if file present, else t2s)
      - text: prompt for t2s
      - image (file): for i2s
      - image_url (str): alternative to file
    """
    mode = None
    prompt = None
    img_bytes = None
    image_url = None

    if request.files:
        mode = request.form.get("mode") or "i2s"
        prompt = request.form.get("text")
        if "image" in request.files:
            img_bytes = request.files["image"].read()
    else:
        data = request.get_json(silent=True) or {}
        mode = (data.get("mode") or "").lower() or None
        prompt = data.get("text")
        image_url = data.get("image_url")

    if mode is None:
        mode = "i2s" if (img_bytes or image_url) else "t2s"

    if mode not in ("t2s", "i2s"):
        return jsonify({"error": "mode must be 't2s' or 'i2s'"}), 400
    if mode == "t2s" and not prompt:
        return jsonify({"error": "text is required for t2s"}), 400
    if mode == "i2s" and not (img_bytes or image_url):
        return jsonify({"error": "image file or image_url required for i2s"}), 400

    job_id = str(uuid.uuid4())
    job = Job(job_id=job_id, mode=mode, prompt=prompt, image_bytes=img_bytes, image_url=image_url)
    JOBS[job_id] = job
    JOB_QUEUE.put(job)

    # ✅ Build external HTTPS URLs from Cloudflare forwarded headers
    proto = request.headers.get("X-Forwarded-Proto") or "https"
    host  = request.headers.get("X-Forwarded-Host") or request.host
    base  = f"{proto}://{host}".rstrip("/")

    status_path   = url_for("get_job", job_id=job_id, _external=False)
    download_path = url_for("download_job", job_id=job_id, _external=False)

    status_url   = base + status_path
    download_url = base + download_path

    return jsonify({"job_id": job_id, "status_url": status_url, "download_url": download_url})

@app.get("/jobs/<job_id>")
def get_job(job_id: str):
    job = JOBS.get(job_id)
    if not job:
        return jsonify({"error": "job not found"}), 404

    proto = request.headers.get("X-Forwarded-Proto") or "https"
    host  = request.headers.get("X-Forwarded-Host") or request.host
    base  = f"{proto}://{host}".rstrip("/")

    download_path = url_for("download_job", job_id=job_id, _external=False)
    download_url  = base + download_path if job.status == "done" else None

    payload = {
        "job_id": job.job_id,
        "status": job.status,
        "message": job.message,
        "created_at": job.created_at,
        "download_url": download_url
    }
    return jsonify(payload)

@app.get("/jobs/<job_id>/download")
def download_job(job_id: str):
    job = JOBS.get(job_id)
    if not job:
        return jsonify({"error": "job not found"}), 404
    if job.status != "done" or not job.out_glb or not os.path.isfile(job.out_glb):
        return jsonify({"error": "not ready"}), 425  # Too Early
    return send_file(job.out_glb, mimetype="model/gltf-binary", as_attachment=True, download_name="scene.glb")

@app.delete("/jobs/<job_id>")
def delete_job(job_id: str):
    job = JOBS.pop(job_id, None)
    if not job:
        return jsonify({"error": "job not found"}), 404
    try:
        if job.out_dir and os.path.isdir(job.out_dir):
            shutil.rmtree(job.out_dir)
    except Exception:
        pass
    return jsonify({"ok": True})

if __name__ == "__main__":
    print(f"🚀 Async WorldGen API on http://0.0.0.0:5000  (device={DEVICE}, output={OUTPUT_ROOT})")
    app.run(host="0.0.0.0", port=5000, threaded=True)
