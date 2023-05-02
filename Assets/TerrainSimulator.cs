using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class TerrainSimulator : MonoBehaviour
{
    [Header("初期化変数")]
    public int terrainWidth = 128;
    public int terrainLength = 128;
    public int subdivisionsX = 512;
    public int subdivisionsZ = 512;
    public int stepCount = 10;
    public int simulationStepPerFrame = 20;

    public GridMesh groundMesh;
    public GridMesh waterMesh;

    // シミュレーションシェーダ
    public ComputeShader simulationShader;

    [Header("シミュレーション制御オブジェクト")]
    public GameObject waterSource;

    [Header("シミュレーション変数")]
    public SimulationConstantBuffer simulationConstant;

    struct GridElement
    {
        // 水量（水面のハイトの決定因子）
        public float waterAmount;

        // 溶解物
        public float dissolvedSandAmount;
        public float dissolvedSoilAmount;
        public float dissolvedStoneAmount;
        // 堆積物（グラウンドのハイトとスプラットの決定因子）
        public float sandAmount;
        public float soilAmount;
        public float stoneAmount;

        public float _padding1;

        // 流速（各グリッドへの拡散/流入量）
        public Vector4 waterVelocity;
    }

    [System.Serializable]
    public struct SimulationConstantBuffer
    {
        // 座標変換用
        public int subdivisionsX;
        public int subdivisionsZ;

        // とりあえず水源は単一
        public float sourceWaterAmount;
        public float sourceWaterRadius;

        // シミュレーション用定数
        public float waterDensity;
        public float gravityConstant;
        public float gridSize;

        // 溶解係数
        public float sandDissolveCoefficient;
        public float soilDissolveCoefficient;
        public float stoneDissolveCoefficient;

        public float deltaTime;
        public float deltaTimeScale;

        public Vector3 sourceWaterSource;
    }

    // GPU用状態変数
    private ComputeBuffer constantBuffer;
    private ComputeBuffer[] buffers = new ComputeBuffer[2];
    private int inputBufferId = 0;

    // 描画出力用（描画用の結果をComputeShaderからここに吐き出す）
    private RenderTexture waterHeightMapTexture;
    private RenderTexture targetWaterHeightMapTexture;
    private RenderTexture groundHeightMapTexture;
    private RenderTexture targetGroundHeightMapTexture;
    private RenderTexture groundSplatTexture;
    private RenderTexture targetGroundSplatTexture;

    private void Start()
    {
        // ComputeBufferを作成する
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = new ComputeBuffer(subdivisionsX * subdivisionsZ, Marshal.SizeOf(typeof(GridElement)), ComputeBufferType.Structured);
        }
        constantBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(SimulationConstantBuffer)), ComputeBufferType.Constant);
        
        simulationConstant.subdivisionsX = subdivisionsX;
        simulationConstant.subdivisionsZ = subdivisionsZ;

        // ComputeBufferを初期化する
        // TODO: perlinNoiseから

        // 描画用のTexture2Dを作成する
        waterHeightMapTexture = CreateTexture2D(0.1f, true);
        targetWaterHeightMapTexture = CreateTexture2D(0.1f, false);
        groundHeightMapTexture = CreateTexture2D(0, true);
        targetGroundHeightMapTexture = CreateTexture2D(0, false);
        groundSplatTexture = CreateTexture2D(0, true);
        targetGroundSplatTexture = CreateTexture2D(0, false);

        // 描画用のグリッドメッシュを初期化する
        InitalizeGrid();

        // BufferInitialize
        int kernelHandle = simulationShader.FindKernel("CSInitBuffer");
        if (kernelHandle == -1)
        {
            Debug.LogError("Kernel not found.");
            return;
        }
        constantBuffer.SetData(new SimulationConstantBuffer[] { simulationConstant });
        simulationShader.SetConstantBuffer("_SimulationConstantBuffer", constantBuffer, 0, Marshal.SizeOf(typeof(SimulationConstantBuffer)));
        simulationShader.SetBuffer(kernelHandle, "simulationBufferIn", buffers[0]);
        simulationShader.SetBuffer(kernelHandle, "simulationBufferOut", buffers[1]);
        simulationShader.Dispatch(kernelHandle, subdivisionsX / 8, subdivisionsZ / 8, 1);
    }

    private void Update()
    {
        for (int i = 0; i < simulationStepPerFrame; i++)
        {
            StepSimulation();
        }

        Graphics.Blit(waterHeightMapTexture, targetWaterHeightMapTexture);
        Graphics.Blit(groundHeightMapTexture, targetGroundHeightMapTexture);
        Graphics.Blit(groundSplatTexture, targetGroundSplatTexture);
    }

    void StepSimulation()
    {
        int kernelHandle = simulationShader.FindKernel("CSMain");
        if (kernelHandle == -1)
        {
            Debug.LogError("Kernel not found.");
            return;
        }

        // Bufferの設定
        int outputBufferId = (inputBufferId + 1) % 2;
        simulationShader.SetBuffer(kernelHandle, "simulationBufferIn", buffers[inputBufferId]);
        simulationShader.SetBuffer(kernelHandle, "simulationBufferOut", buffers[outputBufferId]);
        inputBufferId = outputBufferId;

        // ConstantBufferの設定
        if (waterSource != null)
        {

            simulationConstant.sourceWaterSource = TransformWorldToGridSpace(waterSource.transform.position);
            simulationConstant.sourceWaterSource.y = 0;
            simulationConstant.deltaTime = Time.deltaTime;
        }
        constantBuffer.SetData(new SimulationConstantBuffer[] { simulationConstant });
        simulationShader.SetConstantBuffer("_SimulationConstantBuffer", constantBuffer, 0, Marshal.SizeOf(typeof(SimulationConstantBuffer)));

        // 描画出力テクスチャの設定
        simulationShader.SetTexture(kernelHandle, "waterHeightMapTexture", waterHeightMapTexture);
        simulationShader.SetTexture(kernelHandle, "groundHeightMapTexture", groundHeightMapTexture);
        simulationShader.SetTexture(kernelHandle, "groundSplatTexture", groundSplatTexture);

        simulationShader.Dispatch(kernelHandle, subdivisionsX / 8, subdivisionsZ / 8, 1);
    }

    private Vector3 TransformWorldToGridSpace(Vector3 worldPos)
    {
        var localPos = transform.worldToLocalMatrix * worldPos;
        localPos.x /= terrainWidth;
        localPos.z /= terrainLength;
        return localPos;
    }

    private RenderTexture CreateTexture2D(float initialR, bool isUAV)
    {
        var rt = new RenderTexture(subdivisionsX, subdivisionsX, 1, UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat);
        rt.enableRandomWrite = isUAV;
        rt.Create();
        
        var tex = new Texture2D(subdivisionsX, subdivisionsX, TextureFormat.RFloat, false);
        Color[] colors = new Color[subdivisionsX * subdivisionsX];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = new Color(0f, 0f, 0f, 0f);
        }
        tex.SetPixels(colors);
        tex.Apply();
        Graphics.Blit(tex, rt);
        return rt;
    }

    private void InitalizeGrid()
    {
        groundMesh.CreateMesh(terrainWidth, terrainLength, subdivisionsX, subdivisionsZ);
        var groundMat = groundMesh.GetComponent<Renderer>().material;
        groundMat.SetTexture("_SplatMap", targetGroundSplatTexture);
        groundMat.SetTexture("_HeightMap", targetGroundHeightMapTexture);
        
        waterMesh.CreateMesh(terrainWidth, terrainLength, subdivisionsX, subdivisionsZ);
        var waterMat = waterMesh.GetComponent<Renderer>().material;
        waterMat.SetTexture("_HeightMap", targetWaterHeightMapTexture);
        waterMat.SetTexture("_GroundHeightMap", targetGroundHeightMapTexture);
    }

    private GridElement[,] CreateInitialState()
    {
        var grid = new GridElement[subdivisionsX, subdivisionsZ];

        for (int i = 0; i < subdivisionsX; i++)
        {
            for (int j = 0; j < subdivisionsZ; j++)
            {
                grid[i, j] = new GridElement()
                {

                };
            }
        }
        return null;
    }

    public float[,] GeneratePerlinNoiseMap(int width, int height, float scale, int octaves, float persistence, float lacunarity)
    {
        float[,] noiseMap = new float[width, height];

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        float halfWidth = width / 2f;
        float halfHeight = height / 2f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (x - halfWidth) / scale * frequency;
                    float sampleY = (y - halfHeight) / scale * frequency;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (noiseHeight > maxNoiseHeight)
                {
                    maxNoiseHeight = noiseHeight;
                }
                else if (noiseHeight < minNoiseHeight)
                {
                    minNoiseHeight = noiseHeight;
                }

                noiseMap[x, y] = noiseHeight;
            }
        }

        // Normalize the noise map values to range between 0 and 1
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
            }
        }

        return noiseMap;
    }
}

