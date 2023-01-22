using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BouncePad : MonoBehaviour
{
private void OnTriggerenter(Collider other)
    {
        if(other.tag == "Player")
        {
            AudioManager.instance.PlaySFX(0);
        }
    }
}
