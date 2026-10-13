using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Entry : MonoBehaviour
{
    public Button start;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        start.onClick.AddListener(startGame);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void startGame()
    {
        SceneManager.LoadScene("SampleScene");
    }

    private void OnDestroy()
    {
        start.onClick.RemoveListener(startGame);
    }
}
