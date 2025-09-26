using UnityEngine;

public class CharacterSelector : MonoBehaviour
{
    public GameObject nathanPrefab;
    public GameObject sophiaPrefab;
    public GameObject manuelPrefab;

    public Transform spawnPoint; // 指定角色生成位置

    private GameObject currentCharacter;

    public void SelectNathan()
    {
        SpawnCharacter(nathanPrefab);
    }

    public void SelectSophia()
    {
        SpawnCharacter(sophiaPrefab);
    }

    public void SelectManuel()
    {
        SpawnCharacter(manuelPrefab);
    }

    void SpawnCharacter(GameObject prefab)
    {
        if (currentCharacter != null)
        {
            Destroy(currentCharacter); // 刪除上一個角色
        }

        currentCharacter = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
    }
}
