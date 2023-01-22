using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AmmoPickup : MonoBehaviour
{
    private bool collected;
    // Start is called before the first frame update
private void OnTriggerEnter(Collider other)
    {
        if (other.tag=="Player" && !collected)
        {
            playerController.instance.activeGun.GetAmmo();
            Destroy(gameObject);

            collected = true;

            AudioManager.instance.PlaySFX(3);
        }
    }
}
