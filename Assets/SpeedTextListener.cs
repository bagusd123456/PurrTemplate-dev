using UnityEngine;
using UnityEngine.UI;

public class SpeedTextListener : MonoBehaviour
{
    [SerializeField]
    private Text speedText;

    public static SpeedTextListener Instance { get; private set; }

    public void Awake()
    {
        if (Instance != null & Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public void SetText(float value)
    {
        speedText.text = $"{value}";
    }

    public Text GetTextAsset()
    {
        return speedText;
    }
}
