using UnityEngine;
using System.Collections;
using BulletSharp.SoftBody;
using System;
using BulletSharp;
using System.Collections.Generic;
//using BulletSharp.SoftBody;

namespace BulletUnity
{

  /// <summary>
  /// Used base for any(most) softbodies needing a mesh and meshrenderer.
  /// </summary>
  //[RequireComponent(typeof(MeshFilter))]
  //[RequireComponent(typeof(MeshRenderer))]
  public class BSoftBodyWMesh : BSoftBody
  {
    public BUserMeshSettings meshSettings = new BUserMeshSettings();

    private MeshFilter _meshFilter;
    protected MeshFilter meshFilter
    {
      get { return _meshFilter = _meshFilter ?? GetComponent<MeshFilter>(); }
    }

    [Tooltip("Anchors are Bullet rigid bodies that some soft body nodes/vertices have been bound to. Vertex colors in the Soft Body mesh are used " +
         " to map the nodes/vertices to the anchors. The red channel defines the strength of the anchor. The green channel defines which anchor a" +
          " vertex will be bound to.")]
    public BAnchor[] anchors;

    public BPinnedNode[] pins;

    public float margin = 0.25f;

    // Use this for initialization
    public void BindNodesToAnchors()
    {
      if (transform.localScale != Vector3.one)
      {
        Debug.LogError("The scale must be 1,1,1");
        return;
      }

      if (meshSettings.UserMesh == null)
      {
        Debug.LogError("Must have a selected Mesh");
        return;
      }

      for (int i = 0; i < anchors.Length; i++)
      {
        BAnchor a = anchors[i];
        if (a.colRangeTo <= a.colRangeFrom)
        {
          Debug.LogError("Error with Anchor row " + i + " ColRangeTo must be greater than colRangeFrom.");
        }
        for (int j = i + 1; j < anchors.Length; j++)
        {
          BAnchor b = anchors[j];
          if (b.colRangeFrom >= a.colRangeTo && b.colRangeTo >= a.colRangeTo)
          {
            //good
          }
          else if (b.colRangeFrom <= a.colRangeFrom && b.colRangeTo <= a.colRangeFrom)
          {
            //good
          }
          else
          {
            Debug.LogErrorFormat("The color ranges of Anchors {0} and {1} overlap", i, j);
          }
        }
      }
      //get bones and mesh verts
      //compare these in world space to see which ones line up
      //TODO why does other mesh shape work better than this one.
      Mesh m = meshSettings.UserMesh;
      Vector3[] verts = m.vertices;
      Vector3[] norms = m.normals;
      Color[] cols = m.colors;
      var uvs = m.uv;
      int[] triangles = m.triangles;
      if (cols.Length != verts.Length)
      {
        Debug.LogError("The physics sim mesh had no colors. Colors are needed to identify the anchor bones.");
      }
      //check for duplicate verts
      int numDuplicated = 0;
      for (int i = 0; i < verts.Length; i++)
      {
        for (int j = i + 1; j < verts.Length; j++)
        {
          if (verts[i] == verts[j])
          {
            numDuplicated++;
          }
        }
      }
      if (numDuplicated > 0)
      {
        Debug.LogError("The physics sim mesh has " + numDuplicated + " duplicated vertices. Check that the mesh does not have hard edges and that there are no UVs.");
      }

      // clear old values
      for (int j = 0; j < anchors.Length; j++)
      {
        anchors[j].anchorNodeStrength.Clear();
        anchors[j].anchorPosition.Clear();
      }

      int numAnchorNodes = 0;
      for (int i = 0; i < cols.Length; i++)
      {
        for (int j = 0; j < anchors.Length; j++)
        {
          // Debug.Log($"{cols[i].g} {anchors[j].colRangeFrom} {anchors[j].colRangeTo}");
          if (cols[i].g > anchors[j].colRangeFrom &&
              cols[i].g < anchors[j].colRangeTo)
          {
            anchors[j].anchorNodeStrength.Add(anchors[j].strength);
            anchors[j].anchorPosition.Add(verts[i]);
            numAnchorNodes++;
          }
        }
      }

      SoftBody sb = (SoftBody)m_collisionObject;
      Debug.LogFormat("Done binding nodes to anchors. Found: {0} anchor nodes.", numAnchorNodes);
    }

    public string DescribeAnchors()
    {
      int numAnchorNodes = 0;
      System.Text.StringBuilder sb = new System.Text.StringBuilder();
      if (anchors != null)
      {
        for (int i = 0; i < anchors.Length; i++)
        {
          if (anchors[i].anchorPosition != null)
          {
            numAnchorNodes += anchors[i].anchorPosition.Count;
          }
        }
      }
      return String.Format("{0} anchors have been bound", numAnchorNodes);
    }

    internal override bool _BuildCollisionObject()
    {

      Mesh mesh = meshSettings.Build();
      if (mesh == null)
      {
        Debug.LogError("Could not build mesh from meshSettings for " + this);
        return false;
      }

      GetComponent<MeshFilter>().sharedMesh = mesh;

      if (World == null)
      {
        return false;
      }
      //convert the mesh data to Bullet data and create SoftBody
      BulletSharp.Math.Vector3[] bVerts = new BulletSharp.Math.Vector3[mesh.vertexCount];
      Vector3[] verts = mesh.vertices;
      for (int i = 0; i < mesh.vertexCount; i++)
      {
        bVerts[i] = verts[i].ToBullet();
      }

      SoftBody m_BSoftBody = SoftBodyHelpers.CreateFromTriMesh(World.WorldInfo, bVerts, mesh.triangles);
      m_collisionObject = m_BSoftBody;
      SoftBodySettings.ConfigureSoftBody(m_BSoftBody);         //Set SB settings

      //Set SB position to GO position
      m_BSoftBody.Rotate(transform.rotation.ToBullet());
      m_BSoftBody.Translate(transform.position.ToBullet());
      m_BSoftBody.Scale(transform.localScale.ToBullet());

      /*
      Debug.Log($"Total nodes: {m_BSoftBody.Nodes.Count}");
      var totalAnchors = 0;
      for (int a = 0; a < anchors.Length; a++)
      {
        totalAnchors += anchors[a].anchorPosition.Count;
      }

      Debug.Log($"Anchor definitions: {anchors.Length}, Total anchors: {totalAnchors}");

      var appendedAnchors = 0;
      for (int n = 0; n < m_BSoftBody.Nodes.Count; n++)
      {
        for (int a = 0; a < anchors.Length; a++)
        {
          BAnchor anchor = anchors[a];
          for (int p = 0; p < anchor.anchorPosition.Count; p++)
          {
            var node = m_BSoftBody.Nodes[n];
            var anchorPosition = anchor.anchorPosition[p].ToBullet();

            // Debug.Log($"Node={n}; AnchorSet={a}; Anchor={p}; Node Position={node.Position}; Anchor Position={anchorPosition}");

            if (node.Position == anchorPosition)
            {
              m_BSoftBody.AppendAnchor(n, (RigidBody)anchor.anchorRigidBody.GetCollisionObject(), false, anchor.anchorNodeStrength[p]);
              appendedAnchors++;
            }
          }
        }
      }

      Debug.Log($"Appended {appendedAnchors} anchors.");
      */
      for (int p = 0; p < pins.Length; p++)
      {
        var pin = pins[p];
        for (int n = 0; n < pin.indices.Length; n++)
        {
          var index = pin.indices[n];
          m_BSoftBody.AppendAnchor(index, (RigidBody)pin.pinnedRigidBody.GetCollisionObject(), pin.disableCollision, pin.strength);
          var node = m_BSoftBody.Nodes[index];
          // m_BSoftBody.SetMass(index, 0);
        }
      }

      m_collisionObject.CollisionShape.Margin = margin;

      return true;
    }

    /// <summary>
    /// Create new SoftBody object using a Mesh
    /// </summary>
    /// <param name="position">World position</param>
    /// <param name="rotation">rotation</param>
    /// <param name="mesh">Need to provide a mesh</param>
    /// <param name="buildNow">Build now or configure properties and call BuildSoftBody() after</param>
    /// <param name="sBpresetSelect">Use a particular softBody configuration pre select values</param>
    /// <returns></returns>
    public static GameObject CreateNew(Vector3 position, Quaternion rotation, Mesh mesh, bool buildNow, SBSettingsPresets sBpresetSelect)
    {
      GameObject go = new GameObject("SoftBodyWMesh");
      go.transform.position = position;
      go.transform.rotation = rotation;
      BSoftBodyWMesh BSoft = go.AddComponent<BSoftBodyWMesh>();
      MeshFilter meshFilter = go.AddComponent<MeshFilter>();
      MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
      BSoft.meshSettings.UserMesh = mesh;
      UnityEngine.Material material = new UnityEngine.Material(Shader.Find("Standard"));
      meshRenderer.material = material;

      BSoft.SoftBodySettings.ResetToSoftBodyPresets(sBpresetSelect); //Apply SoftBody settings presets

      if (buildNow)
      {
        BSoft._BuildCollisionObject();  //Build the SoftBody
      }
      go.name = "BSoftBodyWMesh";
      return go;
    }

    /// <summary>
    /// Update Mesh (or line renderer) at runtime, call from Update 
    /// </summary>
    public override void UpdateMesh()
    {
      /*
      var sb = (SoftBody)m_collisionObject;
      for (int p = 0; p < pins.Length; p++)
      {
        var pin = pins[p];
        var rb = (RigidBody)pin.pinnedRigidBody.GetCollisionObject();
      }
      */

      Mesh mesh = meshFilter.sharedMesh;
      if (verts != null && verts.Length > 0)
      {
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.RecalculateBounds();
        transform.SetTransformationFromBulletMatrix(m_collisionObject.WorldTransform);  //Set SoftBody position, No motionstate    
      }
    }

    [Serializable]
    public class BAnchor
    {
      [Tooltip("A Bullet Physics rigid body")]
      public BRigidBody anchorRigidBody;
      [Tooltip("A range in the green channel. Vertices with a vertex color green value in this range will be bound to this anchor")]
      public float colRangeFrom = 0f;
      [Tooltip("A range in the green channel. Vertices with a vertex color green value in this range will be bound to this anchor")]
      public float colRangeTo = 1f;
      public float strength = 1f;
      [HideInInspector]
      public List<float> anchorNodeStrength = new List<float>();
      [HideInInspector]
      public List<Vector3> anchorPosition = new List<Vector3>();
    }

    [Serializable]
    public class BPinnedNode
    {
      public BRigidBody pinnedRigidBody;
      public int[] indices;
      public bool disableCollision = true;
      public float strength = 1f;
    }

    private SoftBodyWorldInfo gizmoFakeWorld;
    private Mesh gizmoMesh;
    private SoftBody gizmoSoftBody;

    public void OnValidate()
    {
      gizmoFakeWorld = new SoftBodyWorldInfo();
      gizmoMesh = meshSettings.Build();
      if (gizmoMesh == null)
      {
        Debug.LogError("Could not build mesh from meshSettings for " + this);
        return;
      }

      //convert the mesh data to Bullet data and create SoftBody
      BulletSharp.Math.Vector3[] bVerts = new BulletSharp.Math.Vector3[gizmoMesh.vertexCount];
      Vector3[] verts = gizmoMesh.vertices;
      for (int i = 0; i < gizmoMesh.vertexCount; i++)
      {
        bVerts[i] = verts[i].ToBullet();
      }

      gizmoSoftBody = SoftBodyHelpers.CreateFromTriMesh(gizmoFakeWorld, bVerts, gizmoMesh.triangles);
    }

    public void OnDrawGizmosSelected()
    {
      if (Application.isPlaying) return;
      if (gizmoSoftBody != null)
      {
        Debug.Log($"Soft body nodes = {gizmoSoftBody.Nodes.Count}; Mesh vertices = {meshSettings.UserMesh.vertexCount}");
        var pinnedList = new List<int>();
        for (int n = 0; n < gizmoSoftBody.Nodes.Count; n++)
        {
          var node = gizmoSoftBody.Nodes[n];

          for (int a = 0; a < anchors.Length; a++)
          {
            var anchor = anchors[a];

            for (int p = 0; p < anchor.anchorPosition.Count; p++)
            {
              var anchorPosition = anchor.anchorPosition[p];
              var bAnchorPosition = anchorPosition.ToBullet();

              if (bAnchorPosition == node.Position)
              {
                Vector3 position = transform.TransformPoint(node.Position.ToUnity());
                Gizmos.DrawWireSphere(position, .01f);
                UnityEditor.Handles.Label(position, $"{n}");
                pinnedList.Add(n);
              }
            }
          }
        }

        Debug.Log($"Found {pinnedList.Count} pinned nodes: {String.Join(",", pinnedList)}");

        Gizmos.color = Color.white;
      }
    }
  }
}