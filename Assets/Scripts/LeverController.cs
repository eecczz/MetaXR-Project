using UnityEngine;

public class LeverController : MonoBehaviour
{
    public Transform door;
    public Transform lever_pivot;
    public float door_offset = 5.0f;
    public float door_open_height = 5.0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        door.localPosition = new Vector3(door.localPosition.x, door_offset + door_open_height * lever_pivot.localRotation.x, door.localPosition.z);
    }
}
