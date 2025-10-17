using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{

    public Transform carTransform;
    [Range(1, 10)]
    public float followSpeed = 2;
    [Range(1, 10)]
    public float lookSpeed = 5;
    private Vector3 _absoluteInitCameraPosition;


    public static CameraFollow Instance { get; private set; }
    private void Awake()
    {
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    void Start()
    {
        var initialCarPosition = Vector3.zero;
        if (carTransform)
        {
            initialCarPosition = carTransform.position;
        }

        var initialCameraPosition = gameObject.transform.position;
        _absoluteInitCameraPosition = initialCameraPosition - initialCarPosition;
    }

    void FixedUpdate()
    {
        if (!carTransform) return;

        //Look at car
        Vector3 _lookDirection = (new Vector3(carTransform.position.x, carTransform.position.y, carTransform.position.z)) - transform.position;
        Quaternion _rot = Quaternion.LookRotation(_lookDirection, Vector3.up);
        transform.rotation = Quaternion.Lerp(transform.rotation, _rot, lookSpeed * Time.deltaTime);

        //Move to car
        Vector3 _targetPos = _absoluteInitCameraPosition + carTransform.transform.position;
        transform.position = Vector3.Lerp(transform.position, _targetPos, followSpeed * Time.deltaTime);
    }

}
