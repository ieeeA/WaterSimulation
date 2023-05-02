using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class GridMesh : MonoBehaviour
{
    private MeshFilter meshFilter;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
    }

    public void CreateMesh(
        int terrainWidth,
        int terrainLength,
        int subdivisionsX,
        int subdivisionsZ)
    {
        int numVerticesX = subdivisionsX + 1;
        int numVerticesZ = subdivisionsZ + 1;

        Vector3[] vertices = new Vector3[numVerticesX * numVerticesZ];
        Vector2[] uvs = new Vector2[numVerticesX * numVerticesZ];
        int[] triangles = new int[6 * subdivisionsX * subdivisionsZ];

        float stepX = ((float)terrainWidth) / subdivisionsX;
        float stepZ = ((float)terrainLength) / subdivisionsZ;

        for (int i = 0; i < numVerticesZ; i++)
        {
            for (int j = 0; j < numVerticesX; j++)
            {
                int index = i * numVerticesX + j;
                vertices[index] = new Vector3(j * stepX, 0, i * stepZ);
                uvs[index] = new Vector2((float)j / subdivisionsX, (float)i / subdivisionsZ);
            }
        }

        for (int i = 0; i < subdivisionsZ; i++)
        {
            for (int j = 0; j < subdivisionsX; j++)
            {
                int index = 6 * (i * subdivisionsX + j);

                triangles[index + 0] = i * numVerticesX + j;
                triangles[index + 2] = i * numVerticesX + j + 1;
                triangles[index + 1] = (i + 1) * numVerticesX + j;

                triangles[index + 3] = i * numVerticesX + j + 1;
                triangles[index + 5] = (i + 1) * numVerticesX + j + 1;
                triangles[index + 4] = (i + 1) * numVerticesX + j;
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        meshFilter.mesh = mesh;
    }
}
