using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Move : MonoBehaviour
{
    [SerializeField] 
    Vector3 target = new Vector3(1, 1, 1);
    [SerializeField] 
    float speed = 1;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, target, Time.deltaTime * speed);
    }
}
