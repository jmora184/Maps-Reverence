using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

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

    private NavMeshAgent nav;
    public NavMeshAgent agent;

    public GameObject canvasObj;
    public bool selectionMode = false;

    public List<QueueFunctions> EventCall = new List<QueueFunctions>();

    GameObject soldiers;

    public class QueueFunctions
    {
        public Action method { get; set; }
        public string Id { get; set; }
        public string objName { get; set; }
    }

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        cam.enabled = false;

        MiniUI.instance.button.SetActive(false);
        MiniUI.instance.moveButton.SetActive(false);
        MiniUI.instance.splitButton.SetActive(false);
        MiniUI.instance.cancelButton.SetActive(false);
        MiniUI.instance.test.SetActive(false);
    }

    // Called by your overlay UI icons
    public void SelectUnit(GameObject clickedGO)
    {
        if (clickedGO == null) return;

        GameObject root = clickedGO; // overlay passes root soldier

        hitName = root.name;

        Debug.Log($"Selected: {hitName} (layer {LayerMask.LayerToName(root.layer)})");

        if (root.layer == LayerMask.NameToLayer("army"))
        {
            MiniUI.instance.button.SetActive(true);
            MiniUI.instance.moveButton.SetActive(true);
            MiniUI.instance.splitButton.SetActive(false);
            MiniUI.instance.cancelButton.SetActive(true);
        }
        else if (root.layer == LayerMask.NameToLayer("team"))
        {
            MiniUI.instance.button.SetActive(true);
            MiniUI.instance.moveButton.SetActive(true);
            MiniUI.instance.splitButton.SetActive(true);
            MiniUI.instance.cancelButton.SetActive(true);
        }
    }

    public void add()
    {
        Debug.Log("addclick");
        GameObject pick1 = GameObject.Find(hitName);
        if (pick1 != null)
            pick1.GetComponent<Renderer>().material.color = Color.red;
        addMode = true;
    }

    public void move()
    {
        GameObject pick1 = GameObject.Find(hitName);
        if (pick1 != null)
            pick1.GetComponent<MeshRenderer>().material.color = Color.red;

        MiniUI.instance.mouseMove.SetActive(true);
        moveMode = true;
    }

    public void split()
    {
        GameObject pick1 = GameObject.Find(hitName);
        if (pick1 != null)
            pick1.GetComponent<MeshRenderer>().material.color = Color.red;

        if (!move8 || !move9)
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
        if (move2 || move3 || move4 || move5 || move6 || move7 || move8 || move9)
            return true;
        return false;
    }

    public string Test()
    {
        Debug.Log("hi");
        return "Hi";
    }

    public void armyToArmy(string hName, string seconds, string Id)
    {
        GameObject army1 = GameObject.Find(hName);
        GameObject army2 = GameObject.Find(seconds);
        if (army1 == null || army2 == null) return;

        List<GameObject> gm = new List<GameObject>();

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == seconds)
            {
                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (soldier == null) continue;

                    NavMeshAgent nav1 = soldier.GetComponent<NavMeshAgent>();
                    if (nav1 == null) continue;

                    gm.Add(soldier);

                    if (Vector3.Distance(soldier.transform.position, army2.transform.position) < 1f)
                        nav1.destination = soldier.transform.position;
                    else
                        nav1.destination = army2.transform.position;

                    if (gm.All(obj => Vector3.Distance(obj.transform.position, army2.transform.position) < 1f))
                    {
                        Debug.Log("done");
                        var found = EventCall.SingleOrDefault(z => z.Id == Id);
                        if (found != null) EventCall.Remove(found);
                    }
                }
            }
        }
    }

    public void moveSingle(string hName, Vector3 holders, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        if (pick1 == null) return;

        GameObject iconSprite = pick1.transform.Find("IconSprite")?.gameObject;
        if (iconSprite != null) iconSprite.layer = LayerMask.NameToLayer("cantClick");

        nav = pick1.GetComponent<NavMeshAgent>();
        if (nav == null) return;

        nav.destination = new Vector3(holders.x, pick1.transform.position.y, holders.z);

        GameObject arrow2 = pick1.transform.Find("DirectionSprite")?.gameObject;

        if (Vector3.Distance(pick1.transform.position, new Vector3(holders.x, pick1.transform.position.y, holders.z)) == 0f)
        {
            if (arrow2 != null) arrow2.SetActive(false);

            nav.destination = pick1.transform.position;

            move2 = false;
            move3 = false;

            var found = EventCall.SingleOrDefault(z => z.Id == Id);
            if (found != null) EventCall.Remove(found);

            queueObjects.RemoveAll(x => x == pick1.name);

            if (iconSprite != null) iconSprite.layer = LayerMask.NameToLayer("army");
        }
        else
        {
            nav.destination = new Vector3(holders.x, 1, holders.z);
        }
    }

    public void moveTeam(string hName, Vector3 holders, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        if (pick1 == null) return;

        List<GameObject> gm = new List<GameObject>();

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                pick1.transform.position = new Vector3(holders.x, pick1.transform.position.y, holders.z);

                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (soldier == null) continue;

                    gm.Add(soldier);

                    GameObject arrow2 = soldier.transform.Find("DirectionSprite")?.gameObject;
                    if (arrow2 != null) arrow2.SetActive(true);

                    nav = soldier.GetComponent<NavMeshAgent>();
                    if (nav == null) continue;

                    nav.destination = new Vector3(holders.x, soldier.transform.position.y, holders.z);
                }

                // done check
                if (gm.Count > 0 && gm.All(obj => Vector3.Distance(obj.transform.position, new Vector3(holders.x, obj.transform.position.y, holders.z)) < 1f))
                {
                    foreach (string z in entry.Value)
                    {
                        soldier = GameObject.Find(z);
                        if (soldier == null) continue;

                        GameObject arrow2 = soldier.transform.Find("DirectionSprite")?.gameObject;
                        if (arrow2 != null) arrow2.SetActive(false);

                        var n = soldier.GetComponent<NavMeshAgent>();
                        if (n != null) n.destination = soldier.transform.position;
                    }

                    move4 = false;
                    Debug.Log("done");

                    var found = EventCall.SingleOrDefault(t => t.Id == Id);
                    if (found != null) EventCall.Remove(found);
                }

                return;
            }
        }
    }

    public void TeamToArmy(string hName, string seconds, string Id, Vector3 holders)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(seconds);
        if (pick1 == null || pick2 == null) return;

        pick1.transform.position = pick2.transform.position;

        List<GameObject> gm = new List<GameObject>();

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (soldier == null) continue;
                    if (soldier.name == pick2.name) continue;

                    NavMeshAgent nav1 = soldier.GetComponent<NavMeshAgent>();
                    if (nav1 == null) continue;

                    gm.Add(soldier);

                    if (Vector3.Distance(soldier.transform.position, pick2.transform.position) < 1f)
                    {
                        nav1.destination = soldier.transform.position;
                        queueObjects.RemoveAll(v => v == pick1.name);
                        move5 = false;
                    }
                    else
                    {
                        nav1.destination = pick2.transform.position;
                    }

                    if (gm.All(obj => Vector3.Distance(obj.transform.position, pick2.transform.position) < 1f))
                    {
                        Debug.Log("done");
                        var found = EventCall.SingleOrDefault(t => t.Id == Id);
                        if (found != null) EventCall.Remove(found);
                    }
                }
            }
        }
    }

    public void ArmyToTeam(string hName, string seconds, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(seconds);
        if (pick1 == null || pick2 == null) return;

        nav = pick1.GetComponent<NavMeshAgent>();
        if (nav == null) return;

        nav.destination = pick2.transform.position;

        if (Vector3.Distance(pick1.transform.position, pick2.transform.position) < 1f)
        {
            nav.destination = pick1.transform.position;
            move6 = false;
            Debug.Log("overmove6");

            var found = EventCall.SingleOrDefault(t => t.Id == Id);
            if (found != null) EventCall.Remove(found);
        }
        else
        {
            nav.destination = pick2.transform.position;
        }
    }

    public void TeamToTeam(string hName, string third, string Id)
    {
        GameObject pick1 = GameObject.Find(hName);
        GameObject pick2 = GameObject.Find(third);
        if (pick1 == null || pick2 == null) return;

        pick1.transform.position = pick2.transform.position;

        List<GameObject> gm = new List<GameObject>();

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                GameObject arrow3 = pick1.transform.Find("child")?.gameObject;
                if (arrow3 != null) arrow3.SetActive(false);

                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (soldier == null) continue;

                    nav = soldier.GetComponent<NavMeshAgent>();
                    if (nav == null) continue;

                    nav.destination = pick2.transform.position;

                    if (Vector3.Distance(soldier.transform.position, pick2.transform.position) < 1f)
                    {
                        nav.destination = soldier.transform.position;
                        move7 = false;
                    }
                    else
                    {
                        nav.destination = pick2.transform.position;
                    }

                    gm.Add(soldier);
                }

                if (gm.Count > 0 && gm.All(obj => Vector3.Distance(obj.transform.position, pick2.transform.position) < 1f))
                {
                    Debug.Log("donemove7");
                    listList.Remove(hName);
                    Destroy(GameObject.Find(hName));

                    var found = EventCall.SingleOrDefault(t => t.Id == Id);
                    if (found != null) EventCall.Remove(found);
                }
            }
        }
    }

    public void SplitEven(string hName, string Id)
    {
        Debug.Log("move9");
        GameObject pick1 = GameObject.Find(hName);
        if (pick1 == null) return;

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                foreach (string x in entry.Value)
                {
                    soldier = GameObject.Find(x);
                    if (soldier == null) continue;

                    if (entry.Value.First() == x)
                    {
                        soldier.layer = LayerMask.NameToLayer("army");

                        GameObject arrow2 = soldier.transform.Find("IconSprite")?.gameObject;
                        if (arrow2 != null) arrow2.SetActive(true);

                        nav = soldier.GetComponent<NavMeshAgent>();
                        if (nav != null)
                            nav.destination = new Vector3(pick1.transform.position.x - 1f, pick1.transform.position.y, pick1.transform.position.z);
                    }
                    else if (entry.Value.Last() == x)
                    {
                        soldier.layer = LayerMask.NameToLayer("army");

                        GameObject arrow2 = soldier.transform.Find("IconSprite")?.gameObject;
                        if (arrow2 != null) arrow2.SetActive(true);

                        nav = soldier.GetComponent<NavMeshAgent>();
                        if (nav == null) continue;

                        Vector3 dest = new Vector3(pick1.transform.position.x + 3f, pick1.transform.position.y, pick1.transform.position.z);

                        if (Vector3.Distance(soldier.transform.position, dest) < 1f)
                        {
                            Debug.Log("move9done");
                            Destroy(GameObject.Find(entry.Key));
                            listList.Remove(entry.Key);

                            var found = EventCall.SingleOrDefault(t => t.Id == Id);
                            if (found != null) EventCall.Remove(found);

                            move9 = false;
                        }
                        else
                        {
                            nav.destination = dest;
                        }
                    }
                }
            }
        }
    }

    public void SplitOdd(string hName, string pick1name, string pick2name, string Id)
    {
        GameObject pick = GameObject.Find(hName);
        GameObject pick1 = GameObject.Find(pick1name);
        GameObject pick2 = GameObject.Find(pick2name);
        if (pick == null || pick1 == null || pick2 == null) return;

        foreach (KeyValuePair<string, List<string>> entry in listList)
        {
            if (entry.Key == pick1.name)
            {
                foreach (string x in entry.Value)
                {
                    GameObject s = GameObject.Find(x);
                    if (s == null) continue;

                    nav = s.GetComponent<NavMeshAgent>();
                    if (nav != null)
                        nav.destination = new Vector3(pick.transform.position.x - 1f, pick.transform.position.y, pick.transform.position.z);
                }
            }

            if (entry.Key == pick2.name)
            {
                foreach (string x in entry.Value)
                {
                    soldier2 = GameObject.Find(x);
                    if (soldier2 == null) continue;

                    nav = soldier2.GetComponent<NavMeshAgent>();
                    if (nav == null) continue;

                    Vector3 dest = new Vector3(pick.transform.position.x + 1f, pick.transform.position.y, pick.transform.position.z);

                    if (Vector3.Distance(soldier2.transform.position, dest) < 1f)
                    {
                        Debug.Log("move8dONE");
                        Destroy(GameObject.Find(pick.name));
                        listList.Remove(pick.name);

                        var found = EventCall.SingleOrDefault(t => t.Id == Id);
                        if (found != null) EventCall.Remove(found);

                        move8 = false;
                    }
                    else
                    {
                        nav.destination = dest;
                    }
                }
            }
        }
    }

    void Update()
    {
        // run queued actions
        foreach (QueueFunctions x in EventCall.ToArray())
        {
            x.method?.Invoke();
        }

        if (cam.enabled == true)
        {
            MiniUI.instance.rawImage.SetActive(false);

            Vector3 mouse = Input.mousePosition;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // regular hit (selection)
            RaycastHit hit;
            Physics.Raycast(ray, out hit, Mathf.Infinity);

            // ground hit for move destination (PLANE on Ground layer)
            bool hasGroundPoint = Physics.Raycast(
                ray,
                out RaycastHit groundHit,
                Mathf.Infinity,
                LayerMask.GetMask("ground2")
            );

            Vector3 groundPoint = hasGroundPoint ? groundHit.point : Vector3.zero;

            // split mode runs regardless of click
            if (splitMode == true)
            {
                GameObject pick1 = GameObject.Find(hitName);
                if (pick1 == null) return;

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
                        f1.Clear();
                        f2.Clear();

                        for (int ii = 0; entry.Value.Count > ii; ii++)
                        {
                            if (ii < num) f1.Add(entry.Value[ii]);
                            else f2.Add(entry.Value[ii]);
                        }
                    }
                }

                if (f1.Count() > 1)
                {
                    j++;
                    objToSpawn = new GameObject("team_s" + j);
                    listList.Add(objToSpawn.name, new List<string>(f1));

                    objToSpawn.AddComponent<BoxCollider>();
                    objToSpawn.AddComponent<MeshRenderer>();
                    objToSpawn.layer = LayerMask.NameToLayer("team");

                    objToSpawn.transform.position = new Vector3(pick1.transform.position.x - 1f, pick1.transform.position.y + 1f, pick1.transform.position.z);

                    GameObject childObj = new GameObject("child");
                    childObj.AddComponent<BoxCollider>();
                    childObj.transform.parent = objToSpawn.transform;
                    childObj.layer = LayerMask.NameToLayer("teamSprite");

                    var colliderS = childObj.GetComponent<BoxCollider>();
                    colliderS.size = new Vector3(1, 1, 0);

                    GameObject childObjText = new GameObject("Text");
                    childObjText.transform.parent = objToSpawn.transform;
                    childObjText.AddComponent<TextMesh>();
                    childObjText.layer = LayerMask.NameToLayer("teamSprite");

                    var tt = childObjText.GetComponent<TextMesh>();
                    tt.text = String.Join("\n", f1);
                    tt.color = Color.black;
                    tt.fontSize = 30;
                    tt.transform.position = new Vector3(pick1.transform.position.x + 3f, pick1.transform.position.y, pick1.transform.position.z);
                    tt.transform.rotation = Quaternion.Euler(new Vector3(90f, 90f, 90f));

                    var spriteRenderer = childObj.AddComponent<SpriteRenderer>();
                    spriteRenderer.transform.position = new Vector3(objToSpawn.transform.position.x + 3f, objToSpawn.transform.position.y, objToSpawn.transform.position.z);
                    spriteRenderer.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                    spriteRenderer.transform.localScale = new Vector3(8f, 8f, 8f);
                    spriteRenderer.sprite = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100");
                }
                else
                {
                    objToSpawn = new GameObject("NONE");
                    if (f1.Count > 0)
                    {
                        GameObject solo1 = GameObject.Find(f1.First());
                        if (solo1 != null)
                        {
                            solo1.layer = LayerMask.NameToLayer("army");
                            nav = solo1.GetComponent<NavMeshAgent>();
                            if (nav != null)
                                nav.destination = new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y, pick1.transform.position.z);

                            GameObject arrow = solo1.transform.Find("IconSprite")?.gameObject;
                            if (arrow != null) arrow.SetActive(true);
                        }
                    }
                }

                if (f2.Count() > 1)
                {
                    j++;
                    objToSpawn2 = new GameObject("team_s" + j);
                    listList.Add(objToSpawn2.name, new List<string>(f2));

                    objToSpawn2.AddComponent<BoxCollider>();
                    objToSpawn2.AddComponent<MeshRenderer>();
                    objToSpawn2.layer = LayerMask.NameToLayer("team");

                    objToSpawn2.transform.position = new Vector3(pick1.transform.position.x + 1f, pick1.transform.position.y + 1f, pick1.transform.position.z);

                    GameObject childObj2 = new GameObject("child");
                    childObj2.AddComponent<BoxCollider>();
                    childObj2.transform.parent = objToSpawn2.transform;
                    childObj2.layer = LayerMask.NameToLayer("teamSprite");

                    var colliderS2 = childObj2.GetComponent<BoxCollider>();
                    colliderS2.size = new Vector3(1, 1, 0);

                    GameObject childObjText2 = new GameObject("Text");
                    childObjText2.transform.parent = objToSpawn2.transform;
                    childObjText2.AddComponent<TextMesh>();
                    childObjText2.layer = LayerMask.NameToLayer("teamSprite");

                    var tt2 = childObjText2.GetComponent<TextMesh>();
                    tt2.text = String.Join("\n", f2);
                    tt2.color = Color.black;
                    tt2.fontSize = 30;
                    tt2.transform.position = new Vector3(pick1.transform.position.x + 10f, pick1.transform.position.y + 10f, pick1.transform.position.z + 10f);
                    tt2.transform.rotation = Quaternion.Euler(new Vector3(90f, 90f, 90f));

                    var spriteRenderer2 = childObj2.AddComponent<SpriteRenderer>();
                    spriteRenderer2.transform.position = new Vector3(objToSpawn2.transform.position.x + 1f, objToSpawn2.transform.position.y + 1f, objToSpawn2.transform.position.z);
                    spriteRenderer2.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                    spriteRenderer2.transform.localScale = new Vector3(8f, 8f, 8f);
                    spriteRenderer2.sprite = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100");
                }
                else
                {
                    objToSpawn2 = new GameObject("NONE");
                }

                y++;
                var sp = "SplitOdd" + y;
                QueueFunctions qf = new QueueFunctions()
                {
                    method = (() => SplitOdd(pick1.name, objToSpawn.name, objToSpawn2.name, sp)),
                    Id = sp
                };
                EventCall.Add(qf);
                move8 = true;

                MiniUI.instance.button.SetActive(false);
                MiniUI.instance.moveButton.SetActive(false);
                MiniUI.instance.splitButton.SetActive(false);
                MiniUI.instance.cancelButton.SetActive(false);

                // Thread.Sleep(500); // DON'T DO THIS IN UNITY

                addMode = false;
                splitMode = false;
            }

            // show test UI when moving queued actions
            MiniUI.instance.test.SetActive(move2 || move3 || move4 || move5 || move6 || move7 || move8 || move9);

            // ===== CLICK HANDLING =====
            if (Input.GetMouseButtonDown(0)) // IMPORTANT: Down, not held
            {
                Debug.Log("CLICK: moveMode=" + moveMode + " addMode=" + addMode +
                          " overUI=" + (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) +
                          " hasGround=" + hasGroundPoint);

                // 1) MOVE MODE: choose destination on plane
                if (moveMode == true)
                {
                    // Only block if a REAL button was clicked
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        var go = EventSystem.current.currentSelectedGameObject;
                        if (go != null && go.GetComponent<UnityEngine.UI.Button>() != null)
                        {
                            Debug.Log("Move click blocked by a UI Button.");
                            return;
                        }
                    }

                    if (!hasGroundPoint)
                    {
                        Debug.Log("No Ground hit. Plane must have collider and be on Ground layer.");
                        return;
                    }

                    Vector3 dest = groundPoint;

                    GameObject pick1 = GameObject.Find(hitName);
                    if (pick1 == null)
                    {
                        Debug.Log("No selected unit. hitName=" + hitName);
                        return;
                    }

                    if (pick1.layer == LayerMask.NameToLayer("team"))
                    {
                        y++;
                        var p = "moveTeam" + y;

                        QueueFunctions q = new QueueFunctions()
                        {
                            method = (() => moveTeam(pick1.name, dest, p)),
                            Id = p,
                            objName = pick1.name
                        };

                        if (EventCall.Any(a => a.objName == pick1.name))
                            EventCall.Remove(EventCall.Single(x => x.objName == pick1.name));

                        EventCall.Add(q);
                    }
                    else if (pick1.layer == LayerMask.NameToLayer("army"))
                    {
                        queueObjects.Add(hitName);

                        GameObject arrow2 = pick1.transform.Find("DirectionSprite")?.gameObject;
                        if (arrow2 != null) arrow2.SetActive(true);

                        Vector3 targetDir = dest - pick1.transform.position;
                        if (targetDir.sqrMagnitude > 0.001f)
                            pick1.transform.rotation = Quaternion.LookRotation(targetDir);

                        y++;
                        var p = "moveSingle" + y;

                        QueueFunctions q = new QueueFunctions()
                        {
                            method = (() => moveSingle(pick1.name, dest, p)),
                            Id = p
                        };

                        EventCall.Add(q);
                    }

                    MiniUI.instance.mouseMove.SetActive(false);
                    moveMode = false;

                    MiniUI.instance.button.SetActive(false);
                    MiniUI.instance.moveButton.SetActive(false);
                    MiniUI.instance.splitButton.SetActive(false);
                    MiniUI.instance.cancelButton.SetActive(false);

                    return; // IMPORTANT: stop click from falling into selection/add
                }

                // 2) SELECTION (when NOT moving)
                if (addMode == false && hit.collider != null)
                {
                    if (hit.transform.gameObject.layer == LayerMask.NameToLayer("army"))
                    {
                        bool alreadyExist = queueObjects.Contains(hit.transform.gameObject.transform.parent.gameObject.name);
                        if (!alreadyExist)
                        {
                            hitName = hit.transform.gameObject.transform.parent.gameObject.name;
                            hit.transform.gameObject.transform.GetComponent<SpriteRenderer>().material.color = Color.red;

                            MiniUI.instance.button.SetActive(true);
                            MiniUI.instance.moveButton.SetActive(true);
                            MiniUI.instance.cancelButton.SetActive(true);

                            Debug.Log("addmodeFalsearmy " + hitName);
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
                            Debug.Log("addmodeFalseteam " + hitName);
                        }
                    }
                }

                // 3) ADD MODE (your existing logic) — still depends on `hit`
                if (addMode == true)
                {
                    GameObject pick1 = GameObject.Find(hitName);
                    if (pick1 != null &&
                        hit.transform.gameObject.layer == LayerMask.NameToLayer("army") &&
                        hit.transform.gameObject.transform.parent.gameObject.name != hitName &&
                        pick1.layer == LayerMask.NameToLayer("army"))
                    {
                        groups.Clear();
                        i++;

                        objToSpawn = new GameObject("team_" + i);
                        objToSpawn.AddComponent<MeshRenderer>();

                        GameObject childObj = new GameObject("child");
                        childObj.layer = LayerMask.NameToLayer("teamSprite");
                        childObj.AddComponent<BoxCollider>();

                        var collider2 = childObj.GetComponent<BoxCollider>();
                        collider2.size = new Vector3(1, 1, 0);
                        collider2.isTrigger = true;

                        GameObject childObjText = new GameObject("Text");
                        childObjText.transform.parent = objToSpawn.transform;

                        objToSpawn.layer = LayerMask.NameToLayer("team");

                        GameObject army1 = GameObject.Find(hitName);
                        GameObject army2 = GameObject.Find(hit.transform.gameObject.transform.parent.gameObject.name);

                        if (army1 != null && army2 != null)
                        {
                            army2.layer = LayerMask.NameToLayer("teamMember");
                            army1.layer = LayerMask.NameToLayer("teamMember");

                            childObjText.AddComponent<TextMesh>();

                            objToSpawn.transform.position = army2.transform.position;

                            List<string> groupss = new List<string>();
                            groupss.Add(hitName);
                            groupss.Add(army2.name);

                            listList.Add(objToSpawn.name, groupss);

                            var tt = childObjText.GetComponent<TextMesh>();
                            tt.text = String.Join("\n", groupss);
                            tt.color = Color.black;
                            tt.fontSize = 30;
                            tt.transform.position = new Vector3(army2.transform.position.x + 3f, army2.transform.position.y, army2.transform.position.z);
                            tt.transform.rotation = Quaternion.Euler(new Vector3(90f, 90f, 90f));

                            childObj.transform.parent = objToSpawn.transform;
                            childObjText.layer = LayerMask.NameToLayer("teamSprite");

                            var spriteRenderer = childObj.AddComponent<SpriteRenderer>();
                            spriteRenderer.transform.position = new Vector3(army2.transform.position.x, army2.transform.position.y + 2, army2.transform.position.z);
                            spriteRenderer.transform.rotation = Quaternion.Euler(new Vector3(-90, 10, 0));
                            spriteRenderer.transform.localScale = new Vector3(8f, 8f, 8f);
                            spriteRenderer.sprite = Resources.Load<Sprite>("Sprites/icons8-starburst-shape-100");

                            GameObject arrow = army1.transform.Find("IconSprite")?.gameObject;
                            GameObject arrow2 = army2.transform.Find("IconSprite")?.gameObject;
                            GameObject arrow3 = army1.transform.Find("DirectionSprite")?.gameObject;

                            if (arrow3 != null) arrow3.SetActive(true);
                            if (arrow != null) arrow.SetActive(false);
                            if (arrow2 != null) arrow2.SetActive(false);

                            y++;
                            var p = "ArmyToArmy" + y;
                            move2 = true;

                            QueueFunctions qq = new QueueFunctions()
                            {
                                method = (() => armyToArmy(pick1.name, objToSpawn.name, p)),
                                Id = p
                            };
                            EventCall.Add(qq);

                            addMode = false;

                            MiniUI.instance.button.SetActive(false);
                            MiniUI.instance.moveButton.SetActive(false);
                            MiniUI.instance.splitButton.SetActive(false);

                            // Thread.Sleep(1000); // DON'T DO THIS IN UNITY
                        }
                    }
                }

                // NOTE: Your other addMode blocks (TeamToArmy / ArmyToTeam / TeamToTeam)
                // are unchanged in your original. If you want, paste the remaining bottom section
                // and I’ll re-insert them cleanly too.
            }
        }
    }
}
