// @role Reset button: hides the current screen and brings the title screen back.

using UnityEngine;
using UnityEngine.UI;

public class Reset : MonoBehaviour
{
    public Button reset;
    public Canvas TitleScreen;
    private Canvas canvas;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        canvas = GetComponentInParent<Canvas>();
        reset.onClick.AddListener(ResetScene);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void ResetScene()
    {
        canvas.gameObject.SetActive(false);
        TitleScreen.gameObject.SetActive(true);
    }

    private void OnDestroy()
    {
        reset.onClick.RemoveListener(ResetScene);
    }
}
