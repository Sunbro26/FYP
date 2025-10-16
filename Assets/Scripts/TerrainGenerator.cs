using UnityEngine;

[ExecuteInEditMode]
// We now also require a MeshCollider component.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class CustomTerrainGeneratorWithCollider : MonoBehaviour
{
    [Header("Plane Geometry Settings")]
    [Tooltip("The size of the plane on the X and Z axes.")]
    public Vector2 planeSize = new Vector2(100, 100);

    [Tooltip("The number of vertices along the width and length of the plane. Higher values mean more detail but lower performance.")]
    public Vector2Int planeResolution = new Vector2Int(100, 100);

    [Header("Base Terrain Settings")]
    [Tooltip("The maximum height of the large terrain features.")]
    [Range(0f, 20f)]
    public float heightScale = 4.0f;

    [Tooltip("The scale of the large terrain features. Smaller values create smoother, larger features.")]
    [Range(0.1f, 100f)]
    public float detailScale = 45.0f;

    [Header("Fine Bump Settings")]
    [Tooltip("The maximum height of the small, fine bumps.")]
    [Range(0f, 2f)]
    public float bumpHeightScale = 0.4f;

    [Tooltip("The scale of the fine bumps. Smaller values create more frequent bumps.")]
    [Range(0.1f, 20f)]
    public float bumpDetailScale = 3.0f;

    [Header("General Settings")]
    [Tooltip("The seed for the random number generator. Different seeds will produce different terrains.")]
    public int seed = 0;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider; // Reference to the MeshCollider component.

    // Using OnValidate to get live updates in the editor is great for iteration.
    void OnValidate()
    {
        GenerateTerrain();
    }

    void OnEnable()
    {
        GenerateTerrain();
    }

    /// <summary>
    /// Generates the terrain mesh and updates the collider to match.
    /// </summary>
    public void GenerateTerrain()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>(); // Get the MeshCollider component.
        
        if (meshFilter == null || meshCollider == null)
        {
            Debug.LogError("Required components are missing. Please ensure a MeshFilter and MeshCollider are attached.");
            return;
        }

        Mesh newMesh = new Mesh { name = "ProceduralTerrainMesh" };

        int xVerts = planeResolution.x + 1;
        int zVerts = planeResolution.y + 1;
        int numVertices = xVerts * zVerts;
        int numTriangles = planeResolution.x * planeResolution.y * 6;

        Vector3[] vertices = new Vector3[numVertices];
        Vector2[] uv = new Vector2[numVertices];
        int[] triangles = new int[numTriangles];

        // --- Generate Vertices and UVs ---
        for (int z = 0; z < zVerts; z++)
        {
            for (int x = 0; x < xVerts; x++)
            {
                int i = z * xVerts + x;
                float xPos = ((float)x / planeResolution.x * planeSize.x) - (planeSize.x / 2f);
                float zPos = ((float)z / planeResolution.y * planeSize.y) - (planeSize.y / 2f);

                float xOffset = transform.position.x + seed;
                float zOffset = transform.position.z + seed;

                float baseXCoord = (xPos + xOffset) / detailScale;
                float baseZCoord = (zPos + zOffset) / detailScale;
                float baseHeight = Mathf.PerlinNoise(baseXCoord, baseZCoord) * heightScale;

                float bumpXCoord = (xPos + xOffset) / bumpDetailScale;
                float bumpZCoord = (zPos + zOffset) / bumpDetailScale;
                float bumpHeight = Mathf.PerlinNoise(bumpXCoord, bumpZCoord) * bumpHeightScale;

                vertices[i] = new Vector3(xPos, baseHeight + bumpHeight, zPos);
                uv[i] = new Vector2((float)x / planeResolution.x, (float)z / planeResolution.y);
            }
        }

        // --- Generate Triangles ---
        int triangleIndex = 0;
        int vertexIndex = 0;
        for (int z = 0; z < planeResolution.y; z++)
        {
            for (int x = 0; x < planeResolution.x; x++)
            {
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + xVerts;
                triangles[triangleIndex + 2] = vertexIndex + 1;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + xVerts;
                triangles[triangleIndex + 5] = vertexIndex + xVerts + 1;

                vertexIndex++;
                triangleIndex += 6;
            }
            vertexIndex++;
        }

        // --- Assign data to the mesh ---
        newMesh.vertices = vertices;
        newMesh.triangles = triangles;
        newMesh.uv = uv;

        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        // --- Assign the generated mesh to the filter and collider ---
        meshFilter.mesh = newMesh;
        meshCollider.sharedMesh = newMesh; // This is the crucial new line!
    }
}