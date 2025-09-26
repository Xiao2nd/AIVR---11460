from flask import Flask, request, jsonify
from PIL import Image
from worldgen import WorldGen
import torch
import open3d as o3d
import os
import gc

app = Flask(__name__)

device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

@app.route('/generate_image', methods=['POST'])
def generate_image():
    if 'image' not in request.files:
        return jsonify({"status": "error", "message": "No image uploaded"}), 400

    try:
        image_file = request.files['image']
        image = Image.open(image_file.stream).convert("RGB")
        # ❌ 不做 resize，保留原圖大小

        # 動態載入 i2s 模型
        worldgen_i2s = WorldGen(mode="i2s", device=device, low_vram=True)
        mesh = worldgen_i2s.generate_world(image=image, return_mesh=True)

        output_path = "/mnt/d/plyoutput/scene_from_unity.ply"
        o3d.io.write_triangle_mesh(output_path, mesh)

        del worldgen_i2s
        gc.collect()

        return jsonify({"status": "success", "output": output_path})
    except Exception as e:
        return jsonify({"status": "error", "message": str(e)}), 500


@app.route('/generate', methods=['POST'])
def generate_world():
    data = request.json
    prompt = data.get("text")

    if not prompt:
        return jsonify({"status": "error", "message": "Missing 'text' field"}), 400

    try:
        worldgen_t2s = WorldGen(mode="t2s", device=device, low_vram=True)
        mesh = worldgen_t2s.generate_world(prompt, return_mesh=True)

        output_path = "/mnt/d/plyoutput/scene_from_unity.ply"
        o3d.io.write_triangle_mesh(output_path, mesh)

        del worldgen_t2s
        gc.collect()

        return jsonify({"status": "success", "output": output_path})
    except Exception as e:
        return jsonify({"status": "error", "message": str(e)}), 500


if __name__ == '__main__':
    print("🚀 Flask server running at http://127.0.0.1:5000")
    app.run(host='127.0.0.1', port=5000)
