using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public GameObject menu1;
    public GameObject menu2;

    // 預設啟用 Menu1
    void Start()
    {
        ShowMenu1();
    }

    public void ShowMenu1()
    {
        if (menu1 != null) menu1.SetActive(true);
        if (menu2 != null) menu2.SetActive(false);
    }

    public void ShowMenu2()
    {
        if (menu1 != null) menu1.SetActive(false);
        if (menu2 != null) menu2.SetActive(true);
    }
}
