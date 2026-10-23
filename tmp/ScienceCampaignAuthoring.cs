// Author saved scenes only in the isolated copy. All geometry remains editable in Unity.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class ScienceCampaignAuthoring
{
    const string Game = "Assets/@Project/@sung_wook/@Game/";
    const string Art = Game + "08_Presentation/ScienceLab/";
    const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/ScienceCampaign/";
    static readonly Color Ink = new Color(.14f,.20f,.23f), Teal = new Color(.16f,.48f,.47f);
    static readonly Color Paper = new Color(.96f,.97f,.95f), Floor = new Color(.36f,.44f,.45f);
    static Sprite square, circle, ring, starIcon;
    static Tile tile;
    static TMP_FontAsset font;
    static List<string> report = new List<string>();

    public static void Apply()
    {
        Guard();
        Directory.CreateDirectory(Art); Directory.CreateDirectory(Output); AssetDatabase.Refresh();
        LoadShapes();
        tile=AssetDatabase.LoadAssetAtPath<Tile>(Art+"Lab Surface.asset");
        if(tile==null) { tile=ScriptableObject.CreateInstance<Tile>(); tile.sprite=square; tile.colliderType=Tile.ColliderType.Grid; AssetDatabase.CreateAsset(tile,Art+"Lab Surface.asset"); }
        for(int stage=1;stage<=5;stage++) Author(stage);
        AssetDatabase.SaveAssets();
        File.WriteAllLines(Output+"Authoring.txt",report);
    }
    static void Guard()
    {
        if(!Application.isBatchMode || Path.GetFullPath(Application.dataPath).Replace('\\','/')!="C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Assets")
            throw new InvalidOperationException("Author only the isolated copy.");
    }
    public static void Polish()
    {
        Guard();
        LoadShapes();
        font=SciencePresentationUiAuthoring.Font;
        for(int stage=1;stage<=5;stage++)
        {
            Scene scene=EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(stage));
            SciencePresentationUiAuthoring.Apply(scene,stage);
            StyleResultAndOverlay(scene);
            FixCamera(scene);
            DisablePreviousTerrain(scene);
            var boot=All<ElectricStageSceneBootstrap>(scene).Single();var bs=new SerializedObject(boot);
            var root=(GameObject)bs.FindProperty("stageRoot").objectReferenceValue;
            var lever=(StageLever2D)bs.FindProperty("lever").objectReferenceValue;
            if(stage>1)StyleLever(lever);
            StarVisual(root.GetComponentsInChildren<ElectricStar2D>(true).Single());
            var goal=root.GetComponentsInChildren<ElectricGoal2D>(true).Single();FitGoal(goal);
            if(stage==4)FitFieldPlates(root.transform);
            var view=All<SciencePresentationView>(scene).Single();view.Preview();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
    }
    static void StyleResultAndOverlay(Scene scene)
    {
        foreach(Canvas canvas in All<Canvas>(scene))
            if(canvas.rootCanvas==canvas && canvas.renderMode!=RenderMode.WorldSpace && canvas.GetComponent<SciencePresentationView>()==null)
            {canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=Mathf.Max(canvas.sortingOrder,100);}
        foreach(var view in All<ElectricGameCompleteView>(scene))
        {
            foreach(var label in view.GetComponentsInChildren<TMP_Text>(true))
            {label.font=font;label.color=Ink;label.fontStyle=FontStyles.Normal;}
            foreach(var image in view.GetComponentsInChildren<Image>(true))
            {
                // The previous panels bake navy into their sprites. Use a white
                // surface so the saved paper/teal colors render without tinting.
                image.sprite=square;image.type=Image.Type.Simple;image.material=null;
                var button=image.GetComponent<Button>();
                if(button!=null)
                {
                    image.color=Teal;var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.88f,.95f,.92f);colors.selectedColor=colors.highlightedColor;button.colors=colors;
                    foreach(var label in button.GetComponentsInChildren<TMP_Text>(true))label.color=Color.white;
                }
                else image.color=image.gameObject==view.gameObject?new Color(.10f,.17f,.18f,.7f):Paper;
            }
        }
    }
    public static void StyleResultsAndBuildQa()
    {
        Guard();LoadShapes();font=SciencePresentationUiAuthoring.Font;
        for(int stage=1;stage<=5;stage++)
        {
            Scene scene=EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(stage));
            StyleResultAndOverlay(scene);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();ScienceCampaignBuild.BuildQa();
    }
    static void LoadShapes(){square=Shape("Unit Square",false,false);circle=Shape("Unit Disc",true,false);ring=Shape("Unit Ring",true,true);starIcon=StarShape();}
    static void StyleLever(StageLever2D lever)
    {
        foreach(var r in lever.GetComponentsInChildren<Renderer>(true))r.enabled=false;
        var mover=lever.GetComponentInChildren<global::LeverMover>(true);if(mover==null)return;
        var data=new SerializedObject(mover);var pivot=(Transform)data.FindProperty("pivot").objectReferenceValue;var grab=(Collider2D)data.FindProperty("grabArea").objectReferenceValue;
        Physics2D.SyncTransforms();Vector3 tip=grab.bounds.center,origin=pivot.position;
        foreach(Transform t in lever.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Lab Lever ")).ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
        Rect(lever.transform,"Lab Lever Base",origin.x,origin.y-.35f,.95f,.65f,Floor,20);
        var axis=Rect(lever.transform,"Lab Lever Axis",origin.x,origin.y,.42f,.42f,Teal,22);axis.sprite=circle;
        var rod=Rect(pivot,"Lab Lever Arm",(origin.x+tip.x)/2,(origin.y+tip.y)/2,Vector3.Distance(origin,tip),.12f,Ink,24);rod.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(tip.y-origin.y,tip.x-origin.x)*Mathf.Rad2Deg);
        var knob=Rect(pivot,"Lab Lever Grip",tip.x,tip.y,.48f,.48f,new Color(.82f,.34f,.25f),26);knob.sprite=circle;
    }
    static void FitGoal(ElectricGoal2D goal)
    {
        var c=goal.GetComponent<BoxCollider2D>();c.offset=Vector2.zero;
        c.size=new Vector2(1.1f/Mathf.Abs(goal.transform.lossyScale.x),1.6f/Mathf.Abs(goal.transform.lossyScale.y));c.isTrigger=true;c.enabled=true;
    }
    static void FitFieldPlates(Transform root)
    {
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(t.name!="Upper plate" && t.name!="Lower plate")continue;
            var c=t.GetComponent<BoxCollider2D>();if(c==null)c=t.gameObject.AddComponent<BoxCollider2D>();
            c.offset=Vector2.zero;c.size=Vector2.one;c.isTrigger=false;c.enabled=true;
            t.tag=t.name=="Lower plate"?"Ground":"Untagged";
        }
    }
    public static void PolishAndCapture(){Polish();ScienceCampaignCapture.RunBatch();}
    static void DisablePreviousTerrain(Scene scene)
    {
        foreach(var map in All<Tilemap>(scene))
            if(map.GetComponent<TilemapCollider2D>()!=null && !map.GetComponentsInParent<Transform>(true).Any(t=>t.name.StartsWith("Science Lab ")))
                map.gameObject.SetActive(false);
    }
    static void FixCamera(Scene scene)
    {
        foreach(var component in All<MonoBehaviour>(scene))
            if(component!=null && component.GetType().Namespace=="Unity.Cinemachine")component.enabled=false;
        var cam=All<Camera>(scene).First(c=>c.CompareTag("MainCamera"));
        cam.orthographic=true;cam.orthographicSize=5.8f;cam.transform.position=new Vector3(0,.5f,-10);
        cam.rect=new Rect(0,.11f,.77f,782f/900f-.11f);cam.backgroundColor=Paper;
    }
    static void Author(int stage)
    {
        Scene scene=EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(stage));
        // Unpack only this scene's copies, retaining all serialized gameplay references.
        foreach(GameObject r in scene.GetRootGameObjects())
            foreach(Transform t in r.GetComponentsInChildren<Transform>(true))
                if(t!=null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    PrefabUtility.UnpackPrefabInstance(t.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        var boot=All<ElectricStageSceneBootstrap>(scene).Single();
        var bs=new SerializedObject(boot);
        var stageRoot=(GameObject)bs.FindProperty("stageRoot").objectReferenceValue;
        foreach(Transform previous in stageRoot.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Science Lab ")).ToArray())
            if(previous!=null)Object.DestroyImmediate(previous.gameObject);
        var ball=(ElectricBall2D)bs.FindProperty("ball").objectReferenceValue;
        var lever=(StageLever2D)bs.FindProperty("lever").objectReferenceValue;
        var star=stageRoot.GetComponentsInChildren<ElectricStar2D>(true).Single();
        var goal=stageRoot.GetComponentsInChildren<ElectricGoal2D>(true).Single();
        var contact=stageRoot.GetComponentsInChildren<ContactCharger2D>(true).FirstOrDefault();
        var distance=stageRoot.GetComponentsInChildren<DistanceLauncher2D>(true).FirstOrDefault();
        var field=stageRoot.GetComponentsInChildren<UniformField2D>(true).FirstOrDefault();
        var voltage=stageRoot.GetComponentsInChildren<VoltageLauncher2D>(true).FirstOrDefault();
        foreach(Renderer r in All<Renderer>(scene))
            if(!r.transform.IsChildOf(ball.transform) && !r.transform.IsChildOf(lever.transform) && r.GetComponentInParent<Canvas>()==null) r.enabled=false;
        foreach(Collider2D c in All<Collider2D>(scene))
        {
            bool keep=c.transform.IsChildOf(ball.transform)||c.transform.IsChildOf(lever.transform)
                ||c.GetComponentInParent<ContactCharger2D>()!=null||c.GetComponent<UniformField2D>()!=null
                ||c.GetComponentInParent<VoltageLauncher2D>()!=null||c.GetComponentInParent<ElectricStar2D>()!=null||c.GetComponentInParent<ElectricGoal2D>()!=null;
            if(!keep)c.enabled=false;
        }
        foreach(var motion in All<ElectricObstacleMotion2D>(scene)) motion.enabled=false;
        DisablePreviousTerrain(scene);
        foreach(Canvas c in All<Canvas>(scene)) if(c.renderMode==RenderMode.WorldSpace)c.gameObject.SetActive(false);
        SciencePresentationUiAuthoring.Apply(scene,stage);
        font=SciencePresentationUiAuthoring.Font;
        Transform lab=new GameObject("Science Lab "+stage.ToString("00")+" - Authored Course").transform;
        lab.SetParent(stageRoot.transform,false); lab.position=Vector3.zero;
        Background(lab,scene);
        // Keep direct mouse control and its original pivot/colliders.
        lever.transform.position=new Vector3(-8f,-3.1f,0);
        foreach(var r in lever.GetComponentsInChildren<SpriteRenderer>(true))
            if(r.enabled) {r.color=Ink;r.sortingOrder=35;}
        if(stage==1) { foreach(var r in lever.GetComponentsInChildren<Renderer>(true))r.enabled=false; if(contact!=null)contact.gameObject.SetActive(false); }
        else Label(lab,"레버 · 드래그",-8, -4.4f,2.8f,.35f,.22f,Ink);
        if(stage>1)StyleLever(lever);
        Vector2 spawn=stage<=2?new Vector2(-6,-3.35f):new Vector2(-3,-3.35f);
        Transform spawnMarker=new GameObject("Experiment Start").transform;spawnMarker.SetParent(lab);spawnMarker.position=spawn;
        bs.FindProperty("spawnPoint").objectReferenceValue=spawnMarker;bs.ApplyModifiedPropertiesWithoutUndo();ball.transform.position=spawn;
        foreach(var c in star.GetComponentsInChildren<Collider2D>(true))c.enabled=true;
        foreach(var c in goal.GetComponentsInChildren<Collider2D>(true))c.enabled=true;
        switch(stage)
        {
            case 1:
                Bench(lab,"Practice Bench",-9,9,-4,.5f); Position(star,1,-2.8f); Position(goal,7,-3);
                Label(lab,"01   움직임",-6,1.8f,5, .5f,.34f,Ink);
                Label(lab,"02   전하 선택",0,1.8f,5,.5f,.34f,Ink);
                Label(lab,"03   관찰 완료",6,1.8f,5,.5f,.34f,Ink);
                ChargeCard(lab,-5,0,"0",new Color(.48f,.53f,.54f));ChargeCard(lab,0,0,"+",new Color(.80f,.28f,.23f));ChargeCard(lab,5,0,"−",new Color(.20f,.42f,.76f));
                Label(lab,"중성",-5,-.9f,2,.4f,.27f,Ink);Label(lab,"양전하",0,-.9f,2,.4f,.27f,Ink);Label(lab,"음전하",5,-.9f,2,.4f,.27f,Ink);
                break;
            case 2:
                Bench(lab,"Contact Bench",-9,9,-4,.5f);Bench(lab,"Observation Step",1,4,-3,1);Bench(lab,"Exit Terrace",5,9,-2.5f,1.5f);
                Position(star,3,-1.8f);Position(goal,7,-1.5f);
                contact.transform.position=new Vector3(-3.4f,-3.7f,0);
                Electrode(lab,contact,null,contact.transform.position);
                Label(lab,"접촉 전",-6,.8f,3,.5f,.32f,Ink);Label(lab,"전원에 연결된 고정 전극",-2.8f,-2.1f,4.5f,.7f,.27f,Ink);
                Label(lab,"접촉 후  →  전하와 힘 관찰",4.1f,.8f,8,.6f,.34f,Ink);
                Line(lab,-6,-1.2f,8,-1.2f,new Color(.73f,.79f,.78f),.025f);
                break;
            case 3:
                Bench(lab,"Return Bench",-9,10,-4.5f,.5f);Bench(lab,"Launch Bench",-5,-1.5f,-4,.5f);
                Bench(lab,"Comparison Landing 1",0,3.6f,-2.2f,.5f);Bench(lab,"Comparison Landing 2",4,7,-1.5f,.5f);Bench(lab,"Result Terrace",7.5f,10,-1,.5f);
                Position(star,5.5f,-.4f);Position(goal,8.7f,0);
                var ds=new SerializedObject(distance); var source=(Transform)ds.FindProperty("source").objectReferenceValue;
                source.position=new Vector3(-3.5f,-3.85f,0);Electrode(lab,null,distance,source.position);
                Label(lab,"고정된 거리 r",-3.4f,-2.3f,3.2f,.5f,.27f,Ink);
                Label(lab,"같은 위치에서 한 변수만 바꾸기",1.5f,3.3f,13,.65f,.42f,Ink);
                Label(lab,"B  기준 저장     →     Q 변경     →     E  발사",1.5f,2.3f,14,.65f,.30f,Teal);
                Ruler(lab,-2,9,.9f);break;
            case 4:
                Bench(lab,"Field Return Bench",-9,9,-4.5f,.5f);Bench(lab,"Observation Shelf",.5f,3,0,.5f);
                Position(star,-1,1.15f);Position(goal,7,-3.5f);
                ConfigureField(lab,field,ball,lever);
                Label(lab,"상승",-4.6f,3.2f,2,.4f,.3f,Ink);Label(lab,"중성으로 하강",-1,3.2f,4,.4f,.3f,Ink);Label(lab,"음전하로 제동",3.4f,3.2f,4,.4f,.3f,Ink);
                break;
            case 5:
                Bench(lab,"Energy Return Bench",-9,10,-4.5f,.5f);Bench(lab,"Launcher Bench",-4.5f,-1.5f,-4,.5f);
                Bench(lab,"Energy Landing 1",-1,2,0,.5f);Bench(lab,"Energy Landing 2",3,6,1,.5f);
                Position(star,4.5f,2.1f);Position(goal,8,-3.5f);
                ConfigureVoltage(lab,voltage);
                Label(lab,"전압을 높이면 발사 속력은?",1.7f,4.5f,14,.6f,.40f,Ink);
                Ruler(lab,-1,6,3.3f);break;
        }
        GoalVisual(goal,lab); StarVisual(star);
        FixCamera(scene);
        foreach(var map in lab.GetComponentsInChildren<Tilemap>())
        {var col=map.GetComponent<TilemapCollider2D>();if(col!=null){col.ProcessTilemapChanges();map.GetComponent<CompositeCollider2D>()?.GenerateGeometry();}}
        Physics2D.SyncTransforms();
        report.Add("Stage "+stage+": "+lab.GetComponentsInChildren<Tilemap>().Length+" authored terrain maps; spawn "+spawn+"; star "+star.transform.position+"; exit "+goal.transform.position);
        EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed");
    }
    static void Background(Transform lab,Scene scene)
    {
        // Preserve the white hex motif as faint drafting-paper texture.
        var hex=All<Tilemap>(scene).FirstOrDefault(m=>m.name.Contains("Hex")||m.GetComponentsInParent<Transform>().Any(t=>t.name=="Hexagon"));
        if(hex!=null){hex.GetComponent<TilemapRenderer>().enabled=true;hex.GetComponent<TilemapRenderer>().sortingOrder=-100;hex.color=Color.white;}
        Rect(lab,"Paper wash",0,0,60,35,new Color(Paper.r,Paper.g,Paper.b,.94f),-90);
        Line(lab,-9.5f,4.8f,10,4.8f,new Color(.72f,.78f,.77f),.018f);
        Label(lab,"ELECTRICITY LAB   /   관찰과 실험",-6.6f,4.45f,6,.35f,.19f,Teal);
    }
    static void Bench(Transform lab,string name,float left,float right,float top,float depth)
    {
        var go=new GameObject(name,typeof(Grid));go.transform.SetParent(lab,false);
        var child=new GameObject("Surface",typeof(Tilemap),typeof(TilemapRenderer),typeof(Rigidbody2D),typeof(TilemapCollider2D),typeof(CompositeCollider2D));child.transform.SetParent(go.transform,false);
        child.transform.position=new Vector3(left,top-depth,0);child.tag="Ground";
        int n=Mathf.Max(1,Mathf.CeilToInt((right-left)/.5f));child.transform.localScale=new Vector3((right-left)/n,depth,1);
        var map=child.GetComponent<Tilemap>();map.color=Floor;for(int i=0;i<n;i++)map.SetTile(new Vector3Int(i,0,0),tile);
        child.GetComponent<TilemapRenderer>().sortingOrder=5;child.GetComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Static;
        child.GetComponent<TilemapCollider2D>().compositeOperation=Collider2D.CompositeOperation.Merge;
        child.GetComponent<CompositeCollider2D>().geometryType=CompositeCollider2D.GeometryType.Polygons;
        Rect(lab,name+" top lip",(left+right)/2,top-.035f,right-left,.07f,Teal,7);
        // Sparse supports communicate a built lab bench without adding invisible collision.
        if(depth<.8f){Rect(lab,name+" left support",left+.25f,top-depth-.35f,.1f,.7f,new Color(.70f,.76f,.74f),-1);Rect(lab,name+" right support",right-.25f,top-depth-.35f,.1f,.7f,new Color(.70f,.76f,.74f),-1);}
    }
    static void ConfigureField(Transform lab,UniformField2D field,ElectricBall2D ball,StageLever2D lever)
    {
        field.transform.position=new Vector3(0,-1,0);field.transform.localScale=Vector3.one;
        var zone=field.GetComponent<BoxCollider2D>();zone.enabled=true;zone.offset=Vector2.zero;zone.size=new Vector2(12,6);zone.isTrigger=true;
        var lower=Rect(lab,"Lower plate",0,-4,12,.14f,new Color(.2f,.42f,.76f),12);
        var upper=Rect(lab,"Upper plate",0,2,12,.14f,new Color(.8f,.28f,.23f),12);
        field.Configure(ball,lever,lower.transform,upper.transform,lower,upper,80f);
        FitFieldPlates(lab);
        var arrows=new List<Transform>();
        for(int x=-5;x<=5;x+=2)
        {var a=new GameObject("Field direction").transform;a.SetParent(lab);a.position=new Vector3(x,-.8f,0);Line(a,0,.8f,0,-.8f,new Color(.7f,.77f,.75f),.025f,true);Line(a,0,-.8f,-.15f,-.55f,new Color(.7f,.77f,.75f),.025f,true);Line(a,0,-.8f,.15f,-.55f,new Color(.7f,.77f,.75f),.025f,true);arrows.Add(a);}
        var view=lab.gameObject.AddComponent<ScienceApparatusView>();var so=new SerializedObject(view);so.FindProperty("field").objectReferenceValue=field;
        var arr=so.FindProperty("fieldArrows");arr.arraySize=arrows.Count;for(int i=0;i<arrows.Count;i++)arr.GetArrayElementAtIndex(i).objectReferenceValue=arrows[i];
        so.FindProperty("upperSign").objectReferenceValue=Label(lab,"+  +  +  +  +  +  +",0,2.35f,11,.35f,.27f,Ink);
        so.FindProperty("lowerSign").objectReferenceValue=Label(lab,"−  −  −  −  −  −  −",0,-4.25f,11,.35f,.27f,Ink);so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void ConfigureVoltage(Transform lab,VoltageLauncher2D launcher)
    {
        var so=new SerializedObject(launcher);var lower=(Transform)so.FindProperty("lowerPlate").objectReferenceValue;var upper=(Transform)so.FindProperty("upperPlate").objectReferenceValue;
        Rect(lab,"Launch lower electrode",lower.position.x,lower.position.y,2.2f,.12f,new Color(.8f,.28f,.23f),15);
        Rect(lab,"Launch upper electrode",upper.position.x,upper.position.y,2.2f,.12f,new Color(.2f,.42f,.76f),15);
        Rect(lab,"Voltage source body",-6,-2.1f,.6f,2.2f,new Color(.75f,.81f,.79f),10);
        var dome=Rect(lab,"Voltage source dome",-6,-.7f,1.6f,1.6f,new Color(.55f,.65f,.64f),12);dome.sprite=circle;
        Line(lab,-6,-3.2f,-4.1f,-3.2f,Ink,.07f);Line(lab,-4.1f,-3.2f,-4.1f,-3.95f,Ink,.07f);
        Label(lab,"전압 공급 장치",-6,-3.65f,3,.35f,.24f,Ink);Label(lab,"발사 영역",-3,-1.25f,3,.4f,.27f,Ink);
    }
    static void Electrode(Transform lab,ContactCharger2D contact,DistanceLauncher2D launcher,Vector3 position)
    {
        var sr=Rect(lab,"Fixed electrode",position.x,position.y,.6f,.32f,new Color(.48f,.53f,.54f),20);
        var label=Label(lab,"0",position.x,position.y+.03f,.5f,.28f,.25f,Color.white);
        var v=sr.gameObject.AddComponent<ScienceApparatusView>();var so=new SerializedObject(v);so.FindProperty("contact").objectReferenceValue=contact;so.FindProperty("launcher").objectReferenceValue=launcher;so.FindProperty("electrode").objectReferenceValue=sr;so.FindProperty("chargeLabel").objectReferenceValue=label;so.ApplyModifiedPropertiesWithoutUndo();
        Line(lab,-7.5f,-4.05f,position.x,-4.05f,Ink,.035f);Line(lab,position.x,-4.05f,position.x,position.y,Ink,.035f);
    }
    static void ChargeCard(Transform p,float x,float y,string sign,Color color){var s=Rect(p,"Charge symbol "+sign,x,y,1.2f,1.2f,color,5);s.sprite=circle;Label(p,sign,x,y,1,1,.65f,Color.white);}
    static void GoalVisual(ElectricGoal2D goal,Transform lab)
    {
        FitGoal(goal);
        Vector3 p=goal.transform.position;var s=Rect(goal.transform,"Completion ring",p.x,p.y,1.35f,1.75f,Teal,22);s.sprite=ring;
        Label(goal.transform,"완료",p.x,p.y,1.3f,.45f,.25f,Teal);
    }
    static void StarVisual(ElectricStar2D star)
    {
        foreach(var r in star.GetComponentsInChildren<SpriteRenderer>(true))r.enabled=false;
        Transform old=star.transform.Find("Observation Star");if(old!=null)Object.DestroyImmediate(old.gameObject);
        Vector3 p=star.transform.position;var r2=Rect(star.transform,"Observation Star",p.x,p.y,.7f,.7f,new Color(.95f,.62f,.13f),25);r2.sprite=starIcon;
        var c=star.GetComponent<CircleCollider2D>();c.offset=Vector2.zero;c.radius=.33f/Mathf.Abs(star.transform.lossyScale.x);c.enabled=true;
    }
    static void Position(Component c,float x,float y){c.transform.position=new Vector3(x,y,0);}
    static void Ruler(Transform p,float left,float right,float y){Line(p,left,y,right,y,new Color(.65f,.73f,.72f),.02f);for(int x=Mathf.CeilToInt(left);x<=right;x++)Line(p,x,y,x,y-.15f,new Color(.65f,.73f,.72f),.02f);}
    static SpriteRenderer Rect(Transform parent,string name,float x,float y,float w,float h,Color color,int order)
    {
        var g=new GameObject(name,typeof(SpriteRenderer));g.transform.SetParent(parent);g.transform.position=new Vector3(x,y,0);g.transform.localScale=new Vector3(w/parent.lossyScale.x,h/parent.lossyScale.y,1);var sr=g.GetComponent<SpriteRenderer>();sr.sprite=square;sr.color=color;sr.sortingOrder=order;return sr;
    }
    static void Line(Transform p,float x1,float y1,float x2,float y2,Color c,float width,bool local=false)
    {Vector3 a=new Vector3(x1,y1,0),b=new Vector3(x2,y2,0);if(local){a=p.TransformPoint(a);b=p.TransformPoint(b);}var s=Rect(p,"Rule",(a.x+b.x)/2,(a.y+b.y)/2,Vector3.Distance(a,b),width,c,-5);s.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg);}
    static TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,float size,Color color)
    {var g=new GameObject(text,typeof(TextMeshPro));g.transform.SetParent(parent);g.transform.position=new Vector3(x,y,-.1f);g.transform.localScale=new Vector3(1/parent.lossyScale.x,1/parent.lossyScale.y,1);var t=g.GetComponent<TextMeshPro>();t.font=font;t.text=text;t.fontSize=size*10;t.color=color;t.alignment=TextAlignmentOptions.Center;t.textWrappingMode=TextWrappingModes.NoWrap;t.rectTransform.sizeDelta=new Vector2(w,h);t.GetComponent<MeshRenderer>().sortingOrder=30;return t;}
    static Sprite Shape(string name,bool disc,bool outline)
    {
        string path=Art+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing!=null)return existing;
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false){name=name+" Texture",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};var colors=new Color[128*128];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64));float a=!disc?1:Mathf.Clamp01(64-d);if(outline)a*=Mathf.Clamp01(d-57);colors[y*128+x]=new Color(1,1,1,a);}texture.SetPixels(colors);texture.Apply();
        AssetDatabase.CreateAsset(texture,Art+name+" Texture.asset");var sprite=Sprite.Create(texture,new Rect(0,0,128,128),new Vector2(.5f,.5f),128);sprite.name=name;AssetDatabase.CreateAsset(sprite,path);return sprite;
    }
    static Sprite StarShape()
    {
        string path=Art+"Observation Star.asset";var old=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(old!=null)return old;
        var points=new Vector2[10];for(int i=0;i<10;i++){float a=(90+i*36)*Mathf.Deg2Rad;float radius=i%2==0?61:27;points[i]=new Vector2(64+Mathf.Cos(a)*radius,64+Mathf.Sin(a)*radius);}
        var pixels=new Color[128*128];for(int y=0;y<128;y++)for(int x=0;x<128;x++){bool inside=false;for(int i=0,j=9;i<10;j=i++)if((points[i].y>y+.5f)!=(points[j].y>y+.5f)&&(x+.5f<(points[j].x-points[i].x)*(y+.5f-points[i].y)/(points[j].y-points[i].y)+points[i].x))inside=!inside;pixels[y*128+x]=inside?Color.white:Color.clear;}
        var texture=new Texture2D(128,128,TextureFormat.RGBA32,false){name="Observation Star Texture",filterMode=FilterMode.Bilinear};texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,Art+"Observation Star Texture.asset");
        var sprite=Sprite.Create(texture,new Rect(0,0,128,128),new Vector2(.5f,.5f),128);sprite.name="Observation Star";AssetDatabase.CreateAsset(sprite,path);return sprite;
    }
    static IEnumerable<T> All<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<T>(true));
}
