using UnityEngine;

public class SimpleBillboard : MonoBehaviour
{
    [SerializeField] private bool _reverseFace = false;
    private Camera _mainCamera;

    private void Start()
    {
        _mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (_mainCamera == null) return;

        transform.rotation = _mainCamera.transform.rotation;

        if (_reverseFace) 
        {
            transform.Rotate(0, 180, 0);
        }
    }
}