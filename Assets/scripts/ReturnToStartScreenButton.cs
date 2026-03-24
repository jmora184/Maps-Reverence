using UnityEngine;
using UnityEngine.SceneManagement;

public class ReturnToStartScreenButton : MonoBehaviour
{
    [SerializeField] private string startSceneName = "StartScreen";

    public void LoadStartScreen()
    {
        if (string.IsNullOrWhiteSpace(startSceneName))
        {
            Debug.LogError("ReturnToStartScreenButton: Start scene name is blank.");
            return;
        }

        SceneManager.LoadScene(startSceneName);
    }
}
