using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupMonolithParticles
{
    public static object Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Run setup in Edit mode.");
        var monolith=GameObject.Find("Energy Monolyth");
        if(!monolith)throw new InvalidOperationException("Monolith missing.");
        var sprite=monolith.GetComponentInChildren<SpriteRenderer>();
        var bounds=sprite.bounds;
        var child=monolith.transform.Find("Rising Sparks");
        if(!child)
        {
            child=new GameObject("Rising Sparks").transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject,"Add monolith sparks");
            child.SetParent(monolith.transform,false);
        }
        child.position=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.12f,monolith.transform.position.z);
        var particles=child.GetComponent<ParticleSystem>();
        if(!particles)particles=Undo.AddComponent<ParticleSystem>(child.gameObject);
        Undo.RecordObject(particles,"Configure monolith sparks");
        particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;
        main.duration=6;main.loop=true;main.prewarm=true;main.playOnAwake=true;
        main.startLifetime=new ParticleSystem.MinMaxCurve(4.5f,6f);
        main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(.045f,.085f);
        main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        var rotation=particles.rotationOverLifetime;rotation.enabled=true;
        rotation.z=new ParticleSystem.MinMaxCurve(-.35f,.35f);
        var palette=new Gradient { mode=GradientMode.Fixed };
        palette.SetKeys(new[]{
            new GradientColorKey(new Color(1f,.28f,.035f),0),
            new GradientColorKey(new Color(1f,.28f,.035f),.333f),
            new GradientColorKey(new Color(.08f,.75f,1f),.666f),
            new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
        main.startColor=new ParticleSystem.MinMaxGradient(palette) { mode=ParticleSystemGradientMode.RandomColor };
        main.maxParticles=32;main.gravityModifier=0;
        main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var emission=particles.emission;emission.enabled=true;emission.rateOverTime=2.5f;
        var shape=particles.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;
        shape.scale=new Vector3(bounds.size.x*.6f,.12f,.02f);
        var velocity=particles.velocityOverLifetime;velocity.enabled=true;
        velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(-.045f,.045f);
        velocity.y=new ParticleSystem.MinMaxCurve(.42f,.62f);
        velocity.z=new ParticleSystem.MinMaxCurve(0f,0f);
        var noise=particles.noise;noise.enabled=true;noise.separateAxes=true;
        noise.strengthX=.45f;noise.strengthY=0;noise.strengthZ=0;
        noise.frequency=.65f;noise.scrollSpeed=.18f;noise.damping=true;
        noise.octaveCount=2;noise.quality=ParticleSystemNoiseQuality.Medium;
        noise.positionAmount=.65f;noise.rotationAmount=0;noise.sizeAmount=0;
        var fade=particles.colorOverLifetime;fade.enabled=true;
        var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(.85f,.65f),new GradientAlphaKey(0,1)});
        fade.color=gradient;
        var size=particles.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,.35f));
        const string path="Assets/GameObjects/MonolithSparks.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Map/OreSparkleTexture.asset"));
            material.SetColor("_Color",new Color(2,2,2,1));
            AssetDatabase.CreateAsset(material,path);
        }
        var renderer=particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial=material;renderer.sortingLayerID=sprite.sortingLayerID;renderer.sortingOrder=sprite.sortingOrder+2;
        EditorUtility.SetDirty(particles);EditorUtility.SetDirty(renderer);
        EditorSceneManager.MarkSceneDirty(monolith.scene);AssetDatabase.SaveAssets();
        particles.Simulate(6,true,true);
        var buffer=new ParticleSystem.Particle[32];int count=particles.GetParticles(buffer);
        if(count<5||count>32)throw new Exception("Unexpected particle count: "+count);
        for(int i=0;i<count;i++)if(buffer[i].GetCurrentSize(particles)<=0)throw new Exception("Invisible particle");
        var cameraObject=new GameObject("Particle preview camera");
        var camera=cameraObject.AddComponent<Camera>();
        var target=new RenderTexture(480,640,24);
        var previous=RenderTexture.active;Texture2D capture=null;
        try
        {
            camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
            camera.orthographic=true;camera.orthographicSize=bounds.extents.y+0.45f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.07f,.13f);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            capture=new Texture2D(480,640,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,480,640),0,0);capture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"MonolithSparksPreview.png"),capture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);
            if(capture)UnityEngine.Object.DestroyImmediate(capture);
            particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        return "Configured and rendered rising sparks. Active particle count: "+count;
    }
}
