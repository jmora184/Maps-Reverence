using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public class CheckpointController : MonoBehaviour
{
    public string cpName;
    private UnityEngine.AI.NavMeshAgent nav;
    public UnityEngine.AI.NavMeshAgent agent;
    // Start is called before the first frame update
    void Start()
    {
        if (PlayerPrefs.HasKey(SceneManager.GetActiveScene().name + "_cp"))
        {
            if (PlayerPrefs.GetString(SceneManager.GetActiveScene().name + "_cp") == cpName)
            {
                playerController.instance.transform.position = transform.position;
                Debug.Log("Player starting" + cpName);
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            PlayerPrefs.SetString(SceneManager.GetActiveScene().name + "_cp", "");
        }
    }

    //private void OnTriggerEnter(Collider other)
    //{
    //    if (other.gameObject.tag == "Player")
    //    {
    //        PlayerPrefs.SetString(SceneManager.GetActiveScene().name + "_cp", cpName);
    //        Debug.Log("Player hit" + cpName);

    //        AudioManager.instance.PlaySFX(1);
    //    }
    //}
    private void OnTriggerEnter(Collider other)
    {

        if (other.gameObject.tag == "AllySprite")
        {
            //PlayerPrefs.SetString(SceneManager.GetActiveScene().name + "_cp", cpName);
            Debug.Log("Player hit" + other.gameObject.transform.parent.gameObject.name);
            GameObject army1 = GameObject.Find(other.gameObject.transform.parent.gameObject.name);
            army1.GetComponent<AllyController>().moveSpeed = 8;
            army1.GetComponent<EnemyHealthController>().currentHealth= 8;
            //AudioManager.instance.PlaySFX(1);
        }
    }
}
