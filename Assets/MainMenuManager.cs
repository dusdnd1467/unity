
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene("unity");
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
