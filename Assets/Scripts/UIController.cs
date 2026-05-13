using UnityEngine;
using UnityEngine.SceneManagement;

public class UIController : MonoBehaviour
{

    public GameObject escMenu;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    public void Continue()
    {
        escMenu.SetActive(false);
    }

    public void Play()
    {
        SceneManager.LoadScene("Game_Scene");
    }

    public void QuitToMenu()
    {
        SceneManager.LoadScene("Menu_Scene");
    }

    public void Exit()
    {
       Application.Quit();
    }
}
