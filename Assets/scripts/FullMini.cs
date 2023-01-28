using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading;
using UnityEngine.AI;
using System;

public class FullMini : MonoBehaviour
{
    public static FullMini instance;
    public Camera cam;
    public Transform camTrans;
    GameObject objToSpawn;
    GameObject objToSpawn2;
    GameObject soldier;
    GameObject soldier2;
    GameObject pickFirst;
    GameObject pickLast;
    public Transform target;
    public float speed;
    public bool adding;
    List<string> groups = new List<string>();
    List<string> f1 = new List<string>();
    List<string> f2 = new List<string>();
    List<string> queueObjects = new List<string>();
    public Dictionary<string, List<string>> listList = new Dictionary<string, List<string>>();
    public Vector3 worldPosition;
    public string hitName;
    public bool addMode = false;
    public bool moveMode = false;
    float timeSinceStarted = 0f;
    public bool move2 = false;
    public bool move3 = false;
    public bool move4 = false;
    public bool move5 = false;
    public bool move6 = false;
    public bool move7 = false;
    public bool move8 = false;
    public bool move9 = false;
    public string first;
    public string second;
    public string second2;
    public string third;
    public Vector3 holder;
    public bool splitMode = false;
    public int i = 0;
    public int j = 0;
    public int y = 0;
    GameObject childObj;
    private UnityEngine.AI.NavMeshAgent nav;
    public UnityEngine.AI.NavMeshAgent agent;
    public GameObject canvasObj;
    public bool selectionMode =false;
    public List<QueueFunctions> EventCall = new List<QueueFunctions>();

    GameObject soldiers;
    public class QueueFunctions
    {
        public Action method { get; set; }

        public string Id { get; set; }

        public string objName { get; set; }
    }

    void Start()
    {
        cam.enabled = false;
        //TestCam.instance.cam.enabled = false;
        //TestCam.instance.cam.enabled = false;
        MiniUI.instance.button.SetActive(false);
        MiniUI.instance.moveButton.SetActive(false);
        MiniUI.instance.splitButton.SetActive(false);
        MiniUI.instance.cancelButton.SetActive(false);
        MiniUI.instance.test.SetActive(false);
    }

    private void Awake()
    {
        instance = this;
    }

    public void add()
    {
        Debug.Log("addclick");
        GameObject pick1 = GameObject.Find(hitName);
        pick1.GetComponent<Renderer>().material.color = Color.red;
        addMode = true;
    }

    public void move()
    {
        GameObject pick1 = GameObject.Find(hitName);
        pick1.GetComponent<MeshRenderer>().material.color = Color.red;
        moveMode = true;
    }

    public void split()
    {
     //remove the function if object picked
        GameObject pick1 = GameObject.Find(hitName);
        pick1.GetComponent<MeshRenderer>().material.color = Color.red;
        if(!move8 || !move9)
        {
            splitMode = true;
        }
        

    }

    public void cancel()
    {
        addMode = false;
        moveMode = false;
        splitMode = false;
        MiniUI.instance.button.SetActive(false);
        MiniUI.instance.moveButton.SetActive(false);
        MiniUI.instance.splitButton.SetActive(false);
        MiniUI.instance.cancelButton.SetActive(false);

    }




    public bool mouseDisable()
    {
        Debug.Log("ran");
        if(move2 || move3 || move4 || move5 || move6 || move7 || move8 || move9)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    public string Test()
    {
        Debug.Log("hi");
        return "Hi";
    }

    public void armyToArmy(string hName,string seconds, string Id)
    {
        
        GameObject army1 = GameObject.Find(hName);
        GameObject army2 = GameObject.Find(seconds);
        //UnityEngine.AI.NavMeshAgent nav1;

        //nav1 = army1.GetComponent<UnityEngine.AI.NavMeshAgent>();
        //nav.SetDestination(secondSoldier.transform.position);

        //nav1.destination = army2.transform.position;
        //foreach (Action func in EventCall)
        //    func();
        GameObject arrow2 = army1.transform.Find("DirectionSprite").gameObject;
        //arrow2.SetActive(true);
        //Debug.Log(army2.transform.position);

        List<GameObject> gm = new List<GameObject>();
        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == seconds)
            {

                foreach (string x in entry.Value)
                {

                    soldier = GameObject.Find(x);
                        UnityEngine.AI.NavMeshAgent nav1;
                        nav1 = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        nav1.destination = army2.transform.position;
                        gm.Add(soldier);
                        if (Vector3.Distance(soldier.transform.position, army2.transform.position) < 1f)
                        {
                            nav1.destination = soldier.transform.position;
                        }
                        else
                        {
                            nav1.destination = army2.transform.position;
                        }
             

                    if (gm.All(obj => Vector3.Distance(obj.transform.position, army2.transform.position) < 1f)) // or .Any to test for ... "any"
                    {
                        Debug.Log("done");

                        EventCall.Remove(EventCall.Single(x => x.Id == Id));
                    }

                }


            }

        }
        //if (Vector3.Distance(army1.transform.position, army2.transform.position) <1f)
        //{
        //    nav1.destination = army1.transform.position;
        //    move2 = false;
        //    arrow2.SetActive(false);
        //    EventCall.Remove(EventCall.Single(x => x.Id == Id));


        //}
        //else
        //{
        //    if (GameObject.Find(seconds) != null)
        //    {
        //         nav1.destination = army2.transform.position;
        //    }
        //    else
        //    {
        //        Debug.Log("change");
        //        nav1.destination = army1.transform.position;
        //        EventCall.Remove(EventCall.Single(x => x.Id == Id));
        //    }
        //}
    }

    public void moveSingle(string hName, Vector3 holders, string Id)
    {
      
        GameObject pick1 = GameObject.Find(hName); //"this" is the child
       
        GameObject iconSprite = pick1.transform.Find("IconSprite").gameObject;
        iconSprite.layer = LayerMask.NameToLayer("cantClick");
        nav = pick1.GetComponent<UnityEngine.AI.NavMeshAgent>();
        nav.destination = new Vector3(holders.x, pick1.transform.position.y, holders.z);
        GameObject arrow2 = pick1.transform.Find("DirectionSprite").gameObject;
        foreach (QueueFunctions x in EventCall)
        {
            Debug.Log(x.Id);
        }
        if (Vector3.Distance(pick1.transform.position, new Vector3(holders.x, pick1.transform.position.y, holders.z)) == 0f)
        {
            arrow2.SetActive(false);
            nav.destination = pick1.transform.position;
            move2 = false;
            move3 = false;
            EventCall.Remove(EventCall.Single(x => x.Id == Id));
            queueObjects.RemoveAll(x => x == pick1.name);
            iconSprite.layer = LayerMask.NameToLayer("army");

        }
        else
        {
        
                nav.destination = new Vector3(holders.x, 1, holders.z);
            
        }
     

    }

    public void moveTeam(string hName, Vector3 holders,string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        List<GameObject> gm = new List<GameObject>();

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                pick1.transform.position = new Vector3(holders.x, pick1.transform.position.y, holders.z);
                foreach (string x in entry.Value)
                {
                  
                    soldier = GameObject.Find(x);
                    gm.Add(soldier);
                    GameObject arrow2 = soldier.transform.Find("DirectionSprite").gameObject;   
                    arrow2.SetActive(true);
                    //soldier.GetComponent<Collider>().transform.position = new Vector3(holder.x, soldier.transform.position.y, holder.z);
                    nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    nav.destination = new Vector3(holders.x, soldier.transform.position.y, holders.z);
                    //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(holder.x, soldier.transform.position.y, holder.z), Time.deltaTime * speed);
                    Debug.Log("How");
                    //MiniUI.instance.button.SetActive(false);
                    //MiniUI.instance.moveButton.SetActive(false);
                    //MiniUI.instance.splitButton.SetActive(false);
                    //MiniUI.instance.cancelButton.SetActive(false);
                }
                if (gm.All(obj => Vector3.Distance(obj.transform.position, new Vector3(holders.x, soldier.transform.position.y, holders.z)) < 1f)) // or .Any to test for ... "any"
                {
                    foreach (string z in entry.Value)
                    {
                        soldier = GameObject.Find(z);
                        GameObject arrow2 = soldier.transform.Find("DirectionSprite").gameObject;
                        arrow2.SetActive(false);
                        nav.destination = soldier.transform.position;
                    }
                    
                    //queueObjects.RemoveAll(x => x == pick1.name);
                    move4 = false;
                    
                    Debug.Log("done");
                    EventCall.Remove(EventCall.Single(x => x.Id == Id));
                  
                }
                else
                {
                    nav.destination = new Vector3(holders.x, soldier.transform.position.y, holders.z);
                }

            }

        }
    }

    public void TeamToArmy(string hName, string seconds, string Id,Vector3 holders)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(seconds);
        pick1.transform.position = pick2.transform.position;
        List<GameObject> gm = new List<GameObject>();
        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {

                foreach (string x in entry.Value)
                {
        
                    soldier = GameObject.Find(x);
                    if(soldier.name != pick2.name)
                    {
                        UnityEngine.AI.NavMeshAgent nav1;
                        nav1 = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        nav1.destination = pick2.transform.position;
                        gm.Add(soldier);
                        if (Vector3.Distance(soldier.transform.position, pick2.transform.position) < 1f)
                      {
                            //Debug.Log("done");
                            nav1.destination = soldier.transform.position;
                            //EventCall.Remove(EventCall.Single(x => x.Id == Id));
                            queueObjects.RemoveAll(x => x == pick1.name);
                            move5 = false;


                            //if all objcts in range
                        }
                        else
                        {
                            nav1.destination = pick2.transform.position;
                        }
                    }

                    if (gm.All(obj => Vector3.Distance(obj.transform.position, pick2.transform.position) < 1f)) // or .Any to test for ... "any"
                    {
                        Debug.Log("done");
                   
                        EventCall.Remove(EventCall.Single(x => x.Id == Id));
                    }

                }


            }

        }
    }

    public void ArmyToTeam(string hName, string seconds, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(seconds);

        nav = pick1.GetComponent<UnityEngine.AI.NavMeshAgent>();
        nav.destination = pick2.transform.position;
        if (Vector3.Distance(pick1.transform.position, pick2.transform.position) < 1f)
        {
            nav.destination = pick1.transform.position;
            // entry.Value.Add(hitName);

            move6 = false;
            Debug.Log("overmove6");
            EventCall.Remove(EventCall.Single(x => x.Id == Id));
        }
        else
        {
            nav.destination = pick2.transform.position;
        }
        //pick1.transform.position = Vector3.MoveTowards(pick1.transform.position, pick2.transform.position, Time.deltaTime * speed);
        //if (pick1.transform.position == pick2.transform.position)
        //{

        //    move6 = false;

        //}
    }
    public void TeamToTeam(string hName, string third, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(third);
        pick1.transform.position = pick2.transform.position;
        List<GameObject> gm = new List<GameObject>();
        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                bool done = false;
                GameObject arrow3 = pick1.transform.Find("child").gameObject;
                arrow3.SetActive(false);
                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    nav.destination = pick2.transform.position;
                    if (Vector3.Distance(soldier.transform.position, pick2.transform.position) < 1f)
                    {
                        nav.destination = soldier.transform.position;
                        //foreach (string z in entry.Value)
                        //{
                        //    soldier = GameObject.Find(z);
                        //    nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                            
                        //}

                        move7 = false;
                    }
                    else
                    {
                        nav.destination = pick2.transform.position;
                    }


                    if (gm.All(obj => Vector3.Distance(obj.transform.position, pick2.transform.position) < 1f)) // or .Any to test for ... "any"
                    {
                        Debug.Log("donemove7");
                        listList.Remove(hName);
                        Destroy(GameObject.Find(hName));
                        EventCall.Remove(EventCall.Single(x => x.Id == Id));
                    }
                    //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(pick2.transform.position.x - 1f, soldier.transform.position.y, pick2.transform.position.z), Time.deltaTime * speed);

                    //soldier = GameObject.Find(x);
                    //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(pick2.transform.position.x - 1f, pick2.transform.position.y - 1f, pick2.transform.position.z - 1f), Time.deltaTime * speed);
                }

                //if (soldier.transform.position.y == pick2.transform.position.y - 1f)
                //{
                //    listList.Remove(hitName);
                //    Destroy(GameObject.Find(hitName));
                //    move7 = false;
                //}

            }

        }
    }

    public void SplitEven(string hName,string Id)
    {
        Debug.Log("move9");
        GameObject pick1 = GameObject.Find(hName);
        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (entry.Value.First() == x)
                    {
                        soldier.layer = LayerMask.NameToLayer("army");
                        soldier = GameObject.Find(x);
                        GameObject arrow2 = soldier.transform.Find("IconSprite").gameObject;
                        arrow2.SetActive(true);
                        nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        nav.destination = new Vector3(pick1.transform.position.x - 1f, pick1.transform.position.y, pick1.transform.position.z);
                        //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y, pick1.transform.position.z), Time.deltaTime * speed);
                    }


                    else if (entry.Value.Last() == x)
                    {
                        soldier.layer = LayerMask.NameToLayer("army");
                        GameObject arrow2 = soldier.transform.Find("IconSprite").gameObject;
                        arrow2.SetActive(true);
                        nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                       // nav.destination = new Vector3(pick1.transform.position.x + 3f, pick1.transform.position.y, pick1.transform.position.z);
                        //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(pick1.transform.position.x + 5f, pick1.transform.position.y, pick1.transform.position.z), Time.deltaTime * speed);
                        if (Vector3.Distance(soldier.transform.position, new Vector3(pick1.transform.position.x + 3f, pick1.transform.position.y, pick1.transform.position.z)) < 1f)
                        {
                            Debug.Log("move9done");
                            Destroy(GameObject.Find(entry.Key));
                            listList.Remove(entry.Key);
                            EventCall.Remove(EventCall.Single(x => x.Id == Id));
                            move9 = false;
                        }
                        else
                        {
                            nav.destination = new Vector3(pick1.transform.position.x + 3f, pick1.transform.position.y, pick1.transform.position.z);
                        }
                    }

                }
                //if (Vector3.Distance(soldier.transform.position, new Vector3(pick1.transform.position.x + 5f, pick1.transform.position.y, pick1.transform.position.z)) < speed * Time.deltaTime)
                //{
                //Destroy(GameObject.Find(entry.Key));
                //move9 = false;
                //}

        
                //if (soldier.transform.position.x == pick1.transform.position.x+5f)
                //{
                //   Destroy(GameObject.Find(entry.Key));
                //    hitName = "";
                //    addMode = false;
                //    move9 = false;
                //}

            }

        }
    }
    public void SplitOdd(string hName, string pick1name, string pick2name, string Id)
    {
       
        GameObject pick = GameObject.Find(hName);
        GameObject pick1 = GameObject.Find(pick1name);
        GameObject pick2 = GameObject.Find(pick2name);
        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)

            {
                foreach (string x in entry.Value)
                {
                    GameObject soldier = GameObject.Find(x);
                    //GameObject arrow2 = soldier.transform.Find("IconSprite").gameObject;
                    //arrow2 .SetActive(true);
                    //soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(pick.transform.position.x + 5f, pick.transform.position.y, pick.transform.position.z), Time.deltaTime * speed);
                    nav = soldier.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    //nav.SetDestination(secondSoldier.transform.position);
                    nav.destination = new Vector3(pick.transform.position.x - 1f, pick.transform.position.y, pick.transform.position.z);
                }


            }
            if (entry.Key == pick2.name)
            {
                //Debug.Log("move8dONE");
                foreach (string x in entry.Value)
                {
                    soldier2 = GameObject.Find(x);
                    //soldier2.transform.position = Vector3.MoveTowards(soldier2.transform.position, new Vector3(pick.transform.position.x - 5f, pick.transform.position.y, pick.transform.position.z), Time.deltaTime * speed);
                    nav = soldier2.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    //nav.destination = new Vector3(pick.transform.position.x +5f, pick.transform.position.y, pick.transform.position.z);
                    if (Vector3.Distance(soldier2.transform.position, new Vector3(pick.transform.position.x + 1f, pick.transform.position.y, pick.transform.position.z)) < 1f)
                    {
                        Debug.Log("move8dONE");
                        Destroy(GameObject.Find(pick.name));
                        listList.Remove(pick.name);

                        EventCall.Remove(EventCall.Single(x => x.Id == Id));
                        move8 = false;

                    }
                    else
                    {
                        nav.destination = new Vector3(pick.transform.position.x + 1f, pick.transform.position.y, pick.transform.position.z);
                    }
                }
   

            }

        }



    }
    void Update()
    {


            foreach (QueueFunctions x in EventCall)
            {                       
                x.method();
            }
        
        //if (Input.GetKeyDown(KeyCode.F))
        //{
        //    Debug.Log(EventCall.Count());
        //}


        if (cam.enabled == true)
        {

            //this.canvasObj.active = false;
            MiniUI.instance.rawImage.SetActive(false);
            GameObject enemy = GameObject.Find("Capsule");
            GameObject enemy2 = GameObject.Find("2nd Cube");

            Vector3 mouse = Input.mousePosition;
           // mouse.z = cam.nearClipPlane;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            float distance;
            Vector3 userInputPosition = cam.ScreenToWorldPoint(mouse);
            //Debug.Log(userInputPosition.z);
            RaycastHit hit;
            //Debug.Log(mouse.y+"vs"+ userInputPosition.y);
            //Debug.Log(mouse.y);
            if (Physics.Raycast(ray, out hit, Mathf.Infinity))
            {

            }
 

        

          
            if (splitMode == true)
            {
        
                GameObject pick1 = GameObject.Find(hitName);
                //2 count
                foreach (KeyValuePair<string, List<string>> entry in listList)
                {
                  
                    if (entry.Key == pick1.name)
                    {
        
                        if (entry.Value.Count == 2)
                        {
                            Debug.Log("return");
                            y++;
                            var u = "SplitEven" + y;

                            QueueFunctions c = new QueueFunctions()
                            {
                                method = (() => SplitEven(pick1.name, u)),
                                Id = u
                            };
                            EventCall.Add(c);
                            move9 = true;
                            addMode = false;
                            splitMode = false;
                            MiniUI.instance.button.SetActive(false);
                            MiniUI.instance.moveButton.SetActive(false);
                            MiniUI.instance.splitButton.SetActive(false);
                            MiniUI.instance.cancelButton.SetActive(false);
                            return;
                        }
                       int num = entry.Value.Count / 2;

                         for (int i = 0; entry.Value.Count > i; i++)
                        {
                            if (i < num)
                            {
                                f1.Add(entry.Value[i]);
                            
                            }
                            else
                            {
                                f2.Add(entry.Value[i]);
                            }
                        }
                      
             
                    }   
                }
                //not 2 ccount
                Debug.Log(f1.Count());
                Debug.Log(f2.Count());
                Debug.Log("did not return");
           
                if (f1.Count() > 1)
                {
                    j++;
                    objToSpawn = new GameObject("team_s" + j);
                    listList.Add(objToSpawn.name, f1);
                    objToSpawn.AddComponent<BoxCollider>();
                    objToSpawn.AddComponent<MeshRenderer>();
                    GameObject childObj = new GameObject("child");
                    //var collider = objToSpawn.transform.gameObject.GetComponent<BoxCollider>();
                    objToSpawn.layer = LayerMask.NameToLayer("team");
                    //collider.size = new Vector3(6, 6, 6);
                    objToSpawn.transform.position = new Vector3(pick1.transform.position.x - 1f, pick1.transform.position.y + 1f, pick1.transform.position.z);
                    childObj.AddComponent<BoxCollider>();
                    GameObject childObjText = new GameObject("Text");

                    childObjText.transform.parent = objToSpawn.transform;
                    childObjText.AddComponent<TextMesh>();
                    var tt = childObjText.transform.gameObject.GetComponent<TextMesh>();
                    var stringList = String.Join("\n", f1);
                    tt.text = stringList;
                    tt.color = Color.black;
                    tt.fontSize = 30;
                    tt.transform.position = pick1.transform.position;
                    tt.transform.rotation = Quaternion.Euler(new Vector3(90f, 0, 90f));
                    childObj.transform.parent = objToSpawn.transform;
                    childObj.layer = LayerMask.NameToLayer("teamSprite");
                    childObjText.layer = LayerMask.NameToLayer("teamSprite");
                    var colliderS = childObj.transform.gameObject.GetComponent<BoxCollider>();
                    colliderS.size = new Vector3(1, 1, 0);
                    var spriteRenderer = childObj.transform.gameObject.AddComponent<SpriteRenderer>();
                    spriteRenderer.transform.position = new Vector3(objToSpawn.transform.position.x - 1f, objToSpawn.transform.position.y + 1f, objToSpawn.transform.position.z);
                    spriteRenderer.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                    spriteRenderer.transform.localScale = new Vector3(8f, 8f, 8f);

                    var texture = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100");
                    spriteRenderer.sprite = texture;
                }
                else
                {
                    objToSpawn = new GameObject("NONE");
                    GameObject solo1 = GameObject.Find(f1.First());
                    solo1.layer=LayerMask.NameToLayer("army");
                    nav = solo1.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    nav.destination = new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y, pick1.transform.position.z);
                    GameObject arrow = solo1.transform.Find("IconSprite").gameObject;     
                    arrow.SetActive(true);
                }
          
                if (f2.Count() >1) {
                          j++;
                    objToSpawn2 = new GameObject("team_s" + j);
                    listList.Add(objToSpawn2.name, f2);
                    objToSpawn2.AddComponent<BoxCollider>();
                    objToSpawn2.AddComponent<MeshRenderer>();
                    //var collider2 = objToSpawn2.transform.gameObject.GetComponent<BoxCollider>();
                    objToSpawn2.layer = LayerMask.NameToLayer("team");
                    //collider2.size = new Vector3(6, 6, 6);

                    objToSpawn2.transform.position = new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y + 1f, pick1.transform.position.z);
                    GameObject childObj2 = new GameObject("child");
                    childObj2.AddComponent<BoxCollider>();
                    GameObject childObjText2 = new GameObject("Text");

                    childObjText2.transform.parent = objToSpawn2.transform;
                    childObjText2.AddComponent<TextMesh>();
                    var tt2 = childObjText2.transform.gameObject.GetComponent<TextMesh>();
                    var stringList2 = String.Join("\n", f2);
                    tt2.text = stringList2;
                    tt2.color = Color.black;
                    tt2.fontSize = 30;
                    tt2.transform.position = objToSpawn2.transform.position;
                    tt2.transform.rotation = Quaternion.Euler(new Vector3(90f, 0, 90f));
                    childObj2.transform.parent = objToSpawn2.transform;
                    childObj2.layer = LayerMask.NameToLayer("teamSprite");
                    childObjText2.layer = LayerMask.NameToLayer("teamSprite");
                    var colliderS2 = childObj2.transform.gameObject.GetComponent<BoxCollider>();
                    colliderS2.size = new Vector3(1, 1, 0);
                    var spriteRenderer2 = childObj2.transform.gameObject.AddComponent<SpriteRenderer>();
                    spriteRenderer2.transform.position = new Vector3(objToSpawn2.transform.position.x + 1f, objToSpawn2.transform.position.y + 1f, objToSpawn2.transform.position.z);
                    spriteRenderer2.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                    spriteRenderer2.transform.localScale = new Vector3(8f, 8f, 8f);

                    var texture2 = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100");
                    spriteRenderer2.sprite = texture2;

                }
                else
                {
                    objToSpawn2 = new GameObject("NONE");
                    GameObject solo = GameObject.Find(f1.First());
                    solo.layer = LayerMask.NameToLayer("army");
                    nav = solo.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    nav.destination = new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y, pick1.transform.position.z);
                    GameObject arrow2 = solo.transform.Find("IconSprite").gameObject;
                    arrow2.SetActive(true);
                }




                //foreach (string x in entry.Value)
                //{
                //    GameObject soldier = GameObject.Find(x);
                //    soldier.transform.position = Vector3.MoveTowards(soldier.transform.position, new Vector3(holder.x, soldier.transform.position.x, holder.z), Time.deltaTime * speed);
                //}
                y++;
                var p = "SplitOdd" + y;

                QueueFunctions x = new QueueFunctions()
                {
                    method = (() => SplitOdd(pick1.name,objToSpawn.name,objToSpawn2.name, p)),
                    Id = p
                };
                EventCall.Add(x);
                move8 = true;
            
                MiniUI.instance.button.SetActive(false);
                MiniUI.instance.moveButton.SetActive(false);
                MiniUI.instance.splitButton.SetActive(false);
                MiniUI.instance.cancelButton.SetActive(false);
                Thread.Sleep(500);
                addMode = false;
                splitMode = false;
            }
            if (move2 || move3 || move4 || move5 || move6 || move7 || move8 || move9)
            {
                MiniUI.instance.test.SetActive(true);
            }
            else
            {
                MiniUI.instance.test.SetActive(false);
            }

            if (Input.GetMouseButton(0))
            {

                //float xPos = enemy.transform.position.x;
                //float yPos = enemy.transform.position.y;
                //float zPos = enemy.transform.position.z;
                //xPos = userInputPosition.x;
                //yPos = userInputPosition.y;
                //zPos = userInputPosition.z;
                Debug.Log(EventCall.Count());


                if (moveMode == true)
                {
                    GameObject pick1 = GameObject.Find(hitName);
              

                    
                    //pick1.transform.position = new Vector3(xPos, pick1.transform.position.x, zPos);
                    holder = userInputPosition;
                    //move3 = true;
                    if (pick1.layer == LayerMask.NameToLayer("team"))
                    {
                        //queueObjects.Add(hitName);
                        y++;
                        var p = "moveTeam" + y;
                        QueueFunctions x = new QueueFunctions()
                        {
                            method = (() => moveTeam(pick1.name, userInputPosition, p)),
                            Id = p,
                            objName =pick1.name
                        };

                        if (EventCall.Any(a => a.objName == pick1.name))
                        {
                            EventCall.Remove(EventCall.Single(x => x.objName == pick1.name));
                        }

                        EventCall.Add(x);
                        //EventCall.Add(() => moveTeam(pick1.name, userInputPosition));
                        moveMode = false;

                    }

                    else if (pick1.layer == LayerMask.NameToLayer("army"))
                    {
                        //move3 = true;  
                        queueObjects.Add(hitName);
                        GameObject arrow2 = pick1.transform.Find("DirectionSprite").gameObject;
                        //arrow2.transform.eulerAngles = new Vector3(90, userInputPosition.y, userInputPosition.z);

                        arrow2.SetActive(true);
                        Vector3 targetDir = userInputPosition - pick1.transform.position;
  
                        float angle = Vector3.Angle(targetDir, pick1.transform.forward);

                        //Debug.Log("Angle of PointA to PointB is " +angle);
                        pick1.transform.rotation = Quaternion.LookRotation(targetDir);
              
                        y++;
                        var p = "moveSingle" + y;
                        QueueFunctions x = new QueueFunctions()
                        {
                            method = (() => moveSingle(pick1.name, userInputPosition, p)),
                            Id = p
                        };
                        EventCall.Add(x);
                        moveMode = false;
                        
                    }
                    MiniUI.instance.button.SetActive(false);
                    MiniUI.instance.moveButton.SetActive(false);
                    MiniUI.instance.splitButton.SetActive(false);
                    MiniUI.instance.cancelButton.SetActive(false);
                    Thread.Sleep(500);
                    //moveMode = false;
                }


                if (addMode == false && moveMode==false && hit.collider !=null)
                {

                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("army"))
                    {
                        bool alreadyExist = queueObjects.Contains(hit.transform.gameObject.transform.parent.gameObject.name);
                        //hitName = hit.transform.gameObject.name;
                        // var parentGameObject = GameObject.Find(hit.transform.gameObject.name);
                        if (!alreadyExist)
                        {
                        hitName = hit.transform.gameObject.transform.parent.gameObject.name;
                        MiniUI.instance.button.SetActive(true);
                        MiniUI.instance.moveButton.SetActive(true);
            
                        MiniUI.instance.cancelButton.SetActive(true);

                        Debug.Log("addmodeFalsearmy" + hitName);
                        }
   
                    }
                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("teamSprite"))
                    {
                        bool alreadyExist = queueObjects.Contains(hit.transform.gameObject.transform.parent.gameObject.name);
                        if (!alreadyExist)
                        {
                            hitName = hit.transform.gameObject.transform.parent.gameObject.name;
                            MiniUI.instance.button.SetActive(true);
                            MiniUI.instance.moveButton.SetActive(true);
                            MiniUI.instance.splitButton.SetActive(true);
                            MiniUI.instance.cancelButton.SetActive(true);
                            Debug.Log("addmodeFalseteam" + hitName);
                        }
                    }

                }


                //if (moveMode == false)
                //{
                //    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("team") && addMode==false)
                //    {

                //        hitName = hit.transform.gameObject.name;
                //        MiniUI.instance.button.SetActive(true);
                //        MiniUI.instance.moveButton.SetActive(true);
                //    }

                //}

                if (addMode == true)
                {
                 
                    GameObject pick1 = GameObject.Find(hitName);
                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("army") && hit.transform.gameObject.transform.parent.gameObject.name != hitName && pick1.layer == LayerMask.NameToLayer("army"))
                    {
                        Debug.Log(hitName);
                        Debug.Log(hit.transform.gameObject.layer);
                        groups.Clear();
                        Debug.Log("Inside1" + pick1.layer);
                        i++;
                        //hitName = hit.transform.gameObject.name;
                        objToSpawn = new GameObject("team_" + i);
                        //objToSpawn.AddComponent<BoxCollider>();
                        objToSpawn.AddComponent<MeshRenderer>();
                        //objToSpawn.AddComponent<NavMeshAgent>();
                        GameObject childObj = new GameObject("child");
                        childObj.layer = LayerMask.NameToLayer("teamSprite");
                        childObj.AddComponent<BoxCollider>();
                        //childObj.sortingOrder = "teamSprite";
                        var collider2 = childObj.transform.gameObject.GetComponent<BoxCollider>();
                        collider2.size = new Vector3(1, 1, 0);
                        collider2.isTrigger = true;

                       
                 
                        GameObject childObjText = new GameObject("Text");
                        
                        childObjText.transform.parent = objToSpawn.transform;
                        //var collider = objToSpawn.transform.gameObject.GetComponent<BoxCollider>();
                        objToSpawn.layer = LayerMask.NameToLayer("team");
                       // collider.size = new Vector3(6, 6, 6);
                        
                        GameObject army1 = GameObject.Find(hitName);
                        GameObject army2 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);
                        army2.layer = LayerMask.NameToLayer("teamMember");
                        army1.layer = LayerMask.NameToLayer("teamMember");
                        childObjText.AddComponent<TextMesh>();
                       

                        //army1.transform.position = new Vector3(army2.transform.position.x - 1f, army2.transform.position.y - 1f, army2.transform.position.z - 1f);
                        second = hit.transform.gameObject.transform.parent.gameObject.name;
                        GameObject pick3 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);
                        //army1.transform.position = target.position;
                        objToSpawn.transform.position = new Vector3(army2.transform.position.x, army2.transform.position.y, army2.transform.position.z);
                        List<string> groupss = new List<string>();
                        groupss.Add(hitName);
                        groupss.Add(hit.transform.gameObject.transform.parent.gameObject.name);
                        listList.Add(objToSpawn.name, groupss);
                        var tt = childObjText.transform.gameObject.GetComponent<TextMesh>();
                        //string[] arr = new string[] { "one", "two", "three", "four" };
                        var stringList = String.Join("\n", groupss);
                        tt.text = stringList;

                        tt.color = Color.black;
                        tt.fontSize = 30;
                        tt.transform.position = army2.transform.position;
                        tt.transform.rotation = Quaternion.Euler(new Vector3(90f, 0, 90f));
                        childObj.transform.parent = objToSpawn.transform;
         
                        childObjText.layer = LayerMask.NameToLayer("teamSprite");
            
                        var spriteRenderer = childObj.transform.gameObject.AddComponent<SpriteRenderer>();
                        spriteRenderer.transform.position = new Vector3(army2.transform.position.x, army2.transform.position.y+2, army2.transform.position.z);
                        spriteRenderer.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                        spriteRenderer.transform.localScale = new Vector3(8f, 8f, 8f);
                        var texture = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100"); 
                       // var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(16, 16));

                        spriteRenderer.sprite = texture;

                        GameObject arrow= army1.transform.Find("IconSprite").gameObject;
                        GameObject arrow2 = army2.transform.Find("IconSprite").gameObject;
                        GameObject arrow3 = army1.transform.Find("DirectionSprite").gameObject;
                        arrow3.SetActive(true);
                        arrow.SetActive(false);
                        arrow2.SetActive(false);
                        //army1.transform.position = Vector3.MoveTowards(army1.transform.position, new Vector3(userInputPosition.x, army2.transform.position.x, userInputPosition.z), Time.deltaTime * speed);
                        //foreach (KeyValuePair<string, List<string>> entry in listList)
                        //{
                        //    Debug.Log(entry.Key);
                        //    foreach (string x in entry.Value)
                        //    {

                        //        Debug.Log(x);

                        //    }

                        //}
                        y++;
                       var p="ArmyToArmy"+y;
                        move2 = true;
                        QueueFunctions x = new QueueFunctions()
                        {
                            method = (()=> armyToArmy(pick1.name, objToSpawn.name, p)),
                            Id = p
                        };
                        EventCall.Add(x);
                        addMode = false;

                        MiniUI.instance.button.SetActive(false);
                        MiniUI.instance.moveButton.SetActive(false);
                        MiniUI.instance.splitButton.SetActive(false);
                        
                        Thread.Sleep(1000);
             
                    }
                }
                if (addMode == true)
                {

                    GameObject pick1 = GameObject.Find(hitName);

                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("army") && hit.transform.gameObject.transform.parent.gameObject.name != hitName && pick1.layer == LayerMask.NameToLayer("team"))
                    {
                        Debug.Log("Inside2hi");
                        second2 = hit.transform.gameObject.transform.parent.gameObject.name;
                        GameObject army2 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);
                        army2.layer = LayerMask.NameToLayer("teamMember");
                        GameObject pick2 = GameObject.Find(second2);
                        GameObject arrow = army2.transform.Find("IconSprite").gameObject;
                        arrow.SetActive(false);
                        foreach (KeyValuePair<string, List<string>> entry in listList)
                        {
                            if (entry.Key == pick1.name)
                            {
                                pickFirst = GameObject.Find(entry.Value.First().ToString());
                                pickLast = GameObject.Find(entry.Value.Last().ToString());
                                entry.Value.Add(second2);

                                var ii = pick1.transform.Find("Text").gameObject;
                                var tt = ii.transform.gameObject.GetComponent<TextMesh>();
                                var stringList = String.Join("\n", entry.Value);
                                tt.text = stringList;
                              
                            }

                        }
                        y++;
                        var p = "TeamToArmy" + y;

                        QueueFunctions x = new QueueFunctions()
                        {
                            method = (() => TeamToArmy(pick1.name, second2, p,userInputPosition)),
                            Id = p
                        };
                        EventCall.Add(x);
                        move5 = true;

                        MiniUI.instance.button.SetActive(false);
                        MiniUI.instance.moveButton.SetActive(false);
                        MiniUI.instance.splitButton.SetActive(false);
                        Thread.Sleep(500);
                      
                        addMode = false;
                    }
                }

                if (addMode == true)
                {
                    GameObject pick1 = GameObject.Find(hitName);

                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("teamSprite") && hit.transform.gameObject.transform.parent.gameObject.name != hitName && pick1.layer == LayerMask.NameToLayer("army"))
                    {
                        Debug.Log("team to army");
                        first = hit.transform.gameObject.transform.parent.gameObject.name;
                        pick1.layer = LayerMask.NameToLayer("teamMember");
                        GameObject team2 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);
                        //GameObject pick2 = GameObject.Find(team2);
                        GameObject arrow = pick1.transform.Find("IconSprite").gameObject;
                        arrow.SetActive(false);
                        foreach (KeyValuePair<string, List<string>> entry in listList)
                        {
                            if (entry.Key == hit.transform.gameObject.transform.parent.gameObject.name)
                            {
                                //pick1.transform.position = new Vector3(team2.transform.position.x - 1f, team2.transform.position.y - 1f, team2.transform.position.z - 1f);
                                entry.Value.Add(hitName);
                                var ii = team2.transform.Find("Text").gameObject;
                                var tt = ii.transform.gameObject.GetComponent<TextMesh>();
                                var stringList = String.Join("\n", entry.Value);
                                tt.text = stringList;

                            }



                        }
                        var p = "TeamToArmy" + y;

                        QueueFunctions x = new QueueFunctions()
                        {
                            method = (() => ArmyToTeam(pick1.name, first, p)),
                            Id = p
                        };
                        EventCall.Add(x);
                        move6 = true;

                        MiniUI.instance.button.SetActive(false);
                        MiniUI.instance.moveButton.SetActive(false);
                        MiniUI.instance.splitButton.SetActive(false); 
                        Thread.Sleep(500);
                        addMode = false;
                    }
                }

              

                if (addMode == true)
                {
                    GameObject pick1 = GameObject.Find(hitName);

                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("teamSprite") && hit.transform.gameObject.transform.parent.gameObject.name != hitName && pick1.layer == LayerMask.NameToLayer("team"))
                    {
                   
                        third = hit.transform.gameObject.transform.parent.gameObject.name;
                        //pick1.layer = LayerMask.NameToLayer("teamMember");
                        GameObject team2 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);
                        //GameObject pick2 = GameObject.Find(team2);
                        List<string> list1 = listList[hitName];
                        List<string> list2 = listList[hit.transform.gameObject.transform.parent.gameObject.name];
                        //var collider = team2.transform.gameObject.GetComponent<BoxCollider>();
                        //collider.size = new Vector3(9, 9, 9);

                        //var resultCollection = list1.Cast<string>().Union(list22.Cast<string>());



                        //foreach(string x in list2)
                        //{
                        //    Debug.Log(x);
                        //}
                        //List<string> f = new List<string>();



                        foreach (KeyValuePair<string, List<string>> entry in listList)
                        {
                            if (entry.Key == hit.transform.gameObject.transform.parent.gameObject.name)
                            {

                                foreach (string x in list1.ToArray())
                                {
                                    //if (!entry.Value.Contains(x))
                                    //{
                                        entry.Value.Add(x);
                                    var ii = team2.transform.Find("Text").gameObject;
                                    var tt = ii.transform.gameObject.GetComponent<TextMesh>();
                                    var stringList = String.Join("\n", entry.Value);
                                    tt.text = stringList;
                                    GameObject arrow2 = pick1.transform.Find("Text").gameObject;
                                    arrow2.SetActive(false);
                                    //GameObject arrow3 = pick1.transform.Find("IconSprite").gameObject;
                                    //arrow3.SetActive(false);
                                    //}


                                }

                            }

                        }


                        var p = "TeamToTeam" + y;

                        QueueFunctions s = new QueueFunctions()
                        {
                            method = (() => TeamToTeam(pick1.name, third, p)),
                            Id = p
                        };
                        EventCall.Add(s);
                        move7 = true;
                      
              
                        MiniUI.instance.button.SetActive(false);
                        MiniUI.instance.moveButton.SetActive(false);
                        MiniUI.instance.splitButton.SetActive(false);

                        Thread.Sleep(500);
                        addMode = false;
                    }
                }

            }

        }
    }



}


