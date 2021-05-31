using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Unity.Collections;

namespace SIM
{
    public class MPMBurst : MonoBehaviour
    {
        public struct Particle
        {
            public float3 x;
            public float3 v;
            public float m;
            public float3x3 C;
            public float3x3 F;
            public float volume_0;
        }

        public struct Cell
        {
            public float3 v;
            public float m;
        }

        public struct Timers
        {
            public float P2G;
            public float GridVelocityUpdate;
            public float G2P;
            public float ResetGrid;
            public float Total;
        }

        public int grid_res = 16;
        int num_cells;
        enum Interpolation
        {
            Quadratic = 0,
            Cubic = 1
        }
        readonly static Interpolation interpolation = Interpolation.Quadratic;
        int i_start;
        float i_translation;
        float D_inv;
        float dt;

        [NonSerialized] public float particle_radius = 1f;
        [NonSerialized] public int[] indices;
        [NonSerialized] public int num_par;
        
        [SerializeField] int job_size = 16;
        [SerializeField] public int num_threads = 15;

        NativeArray<Particle> ps;
        NativeArray<Cell> grid;
        NativeArray<float4> source;

        FluidRender fluid_render = null;
        Stopwatch stopwatch;
        [NonSerialized] public Timers timers = new Timers();

        [SerializeField] public Material fluid_material = null;
        [SerializeField] float particle_spacing = 0.5f;
        [SerializeField] float3 box_size = new float3(1f, 1f, 1f);
        [SerializeField] float3 box_position = new float3(0, 0, 0);
        [SerializeField] float3 gravity = new float3(0.0f, -9.81f, 0.0f);
        [SerializeField] bool apply_gravity = true;
        [SerializeField] bool show_info = true;
        [SerializeField] bool show_timers = true;
        [SerializeField] bool is_fluid = false;
        [SerializeField] bool use_mt = true;

        [SerializeField] float rest_density = 50.0f;
        [SerializeField] float eos_stiffness = 10.0f;
        [SerializeField] float eos_power = 7.0f;
        [SerializeField] float dynamic_viscosity = 0.1f;

        [SerializeField] float lambda = 10.0f;
        [SerializeField] float mu = 20.0f;

        const float mouse_radius = 10;
        bool mouse_down = false;
        float3 mouse_position;

        #region Startup

        void MakeBox(ref List<float3> positions, float spacing, float x, float y, float z, float box_x = 1f, float box_y = 1f, float box_z = 1f)
        {
            for (float i = 0; i < box_x; i += spacing)
            {
                for (float j = 0; j < box_y; j += spacing)
                {
                    for (float k = 0; k < box_z; k += spacing)
                    {
                        float3 p = new float3(x + i, y + j, z + k);
                        positions.Add(p);
                    }
                }
            }
        }
        public void Setup(float spacing, bool random_velocity = false)
        {
            List<float3> temp_positions = new List<float3>();

            MakeBox(ref temp_positions, spacing, box_position.x, box_position.y, box_position.z, box_size.x, box_size.y, box_size.z);

            num_par = temp_positions.Count;
            num_cells = grid_res * grid_res * grid_res;
            ps = new NativeArray<Particle>(num_par, Allocator.Persistent);
            source = new NativeArray<float4>(num_par, Allocator.Persistent);
            indices = new int[num_par];
            for (int i = 0; i < num_par; i++)
            {
                Particle p = new Particle
                {
                    x = temp_positions[i],
                    v = float3.zero,
                    m = 1.0f,
                    C = 0.0f,
                    F = float3x3.identity,
                    volume_0 = 1
                };
                if (random_velocity)
                {
                    p.v = new float3(UnityEngine.Random.Range(0, 10.0f), UnityEngine.Random.Range(0, 10.0f), UnityEngine.Random.Range(0, 10.0f));
                }
                ps[i] = p;

                source[i] = new float4(ps[i].x.x, ps[i].x.y, ps[i].x.z, 1.0f);
                indices[i] = i;

            }

            grid = new NativeArray<Cell>(num_cells, Allocator.Persistent);

            for (int i = 0; i < num_cells; i++)
            {
                var c = new Cell
                {
                    v = float3.zero,
                    m = 0.0f
                };
                grid[i] = c;
            }

            if (interpolation == Interpolation.Cubic)
            {
                i_start = -1;
                i_translation = 0.5f;
                D_inv = 3;
            }
            else
            {
                i_start = 0;
                i_translation = 0.5f;
                D_inv = 4;
            }

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount = num_threads;

            new Job_P2G()
            {
                grid = grid,
                ps = ps,
                num_par = num_par,
                i_start = i_start,
                grid_res = grid_res,
                i_translation = i_translation,
                D_inv = D_inv,
                dt = dt,
                is_fluid = is_fluid,
                eos_stiffness = eos_stiffness,
                eos_power = eos_power,
                rest_density = rest_density,
                dynamic_viscosity = dynamic_viscosity,
                mu = mu,
                lambda = lambda
            }.Schedule().Complete();
            for (int i = 0; i < num_par; i++)
            {
                var p = ps[i];

                float density = 0;
                for (int gx = i_start; gx < 3; gx++)
                {
                    for (int gy = i_start; gy < 3; gy++)
                    {
                        for (int gz = i_start; gz < 3; gz++)
                        {
                            uint3 cell = new uint3
                            (
                                (uint)(p.x.x + gx - i_translation),
                                (uint)(p.x.y + gy - i_translation),
                                (uint)(p.x.z + gz - i_translation)
                            );
                            float3 cell_dist = (p.x - cell);
                            float w = GetWeight(cell_dist);
                            int cell_idx = ((int)cell.z * grid_res * grid_res) + ((int)cell.y * grid_res) + (int)cell.x;
                            density += grid[cell_idx].m * w;
                        }
                    }
                }
                p.volume_0 = p.m / density;
                p.volume_0 = math.clamp(p.volume_0, 1f, 10000f);
                ps[i] = p;
            }
        }

        #endregion

        // Start is called before the first frame update
        void Start()
        {
            dt = Time.fixedDeltaTime;
            if (box_position.x + box_size.x >= grid_res)
            {
                UnityEngine.Debug.LogError("Box x side is outside of grid");
                UnityEngine.Debug.Break();
            }
            else if (box_position.y + box_size.y >= grid_res)
            {
                UnityEngine.Debug.LogError("Box y side is outside of grid");
                UnityEngine.Debug.Break();
            }
            else if (box_position.z + box_size.z >= grid_res)
            {
                UnityEngine.Debug.LogError("Box z side is outside of grid");
                UnityEngine.Debug.Break();
            }
            else
            {
                Setup(particle_spacing);
            }

            fluid_render = new GameObject("FluidRender").AddComponent<FluidRender>();

            if (fluid_render)
            {
                fluid_render.UpdateMesh();
            }
        }

        // FixedUpdate is called once per fixed delta time
        void FixedUpdate()
        {
            SetMousePosition();

            if (show_timers)
            {
                SimulateWithTimers();
            }
            else
            {
                Simulate();
            }
            if (fluid_render)
            {
                fluid_render.UpdateMesh();
            }
        }

        void SetMousePosition()
        {
            mouse_down = false;

            if (Input.GetMouseButton(0))
            {
                mouse_down = true;
                Plane plane = new Plane(Vector3.up, ps[0].x);

                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (plane.Raycast(ray, out float distance))
                {
                    mouse_position = ray.GetPoint(distance);
                    UnityEngine.Debug.Log(mouse_position);
                }
            }
        }

        #region Simulation

        void Simulate()
        {
            if (use_mt)
            {
                new Job_P2G_MT()
                {
                    grid = grid,
                    ps = ps,
                    num_par = num_par,
                    i_start = i_start,
                    grid_res = grid_res,
                    i_translation = i_translation,
                    D_inv = D_inv,
                    dt = dt,
                    is_fluid = is_fluid,
                    eos_stiffness = eos_stiffness,
                    eos_power = eos_power,
                    rest_density = rest_density,
                    dynamic_viscosity = dynamic_viscosity,
                    mu = mu,
                    lambda = lambda
                }.Schedule(num_par, job_size).Complete();
            }
            else
            {
                new Job_P2G()
                {
                    grid = grid,
                    ps = ps,
                    num_par = num_par,
                    i_start = i_start,
                    grid_res = grid_res,
                    i_translation = i_translation,
                    D_inv = D_inv,
                    dt = dt,
                    is_fluid = is_fluid,
                    eos_stiffness = eos_stiffness,
                    eos_power = eos_power,
                    rest_density = rest_density,
                    dynamic_viscosity = dynamic_viscosity,
                    mu = mu,
                    lambda = lambda
                }.Schedule().Complete();
            }

            new Job_GridVelocityUpdate()
            {
                grid = grid,
                grid_res = grid_res,
                apply_gravity = apply_gravity,
                gravity = gravity,
                dt = dt
            }.Schedule(num_cells, job_size).Complete();


            new Job_G2P()
            {
                ps = ps,
                grid = grid,
                i_start = i_start,
                i_translation = i_translation,
                D_inv = D_inv,
                grid_res = grid_res,
                dt = dt,
                mouse_down = mouse_down,
                mouse_position = mouse_position
            }.Schedule(num_par, job_size).Complete();

            new Job_ResetGrid()
            {
                grid = grid
            }.Schedule(num_cells, job_size).Complete();
        }

        public void SimulateWithTimers()
        {
            stopwatch = Stopwatch.StartNew();
            if (use_mt)
            {
                new Job_P2G_MT()
                {
                    grid = grid,
                    ps = ps,
                    num_par = num_par,
                    i_start = i_start,
                    grid_res = grid_res,
                    i_translation = i_translation,
                    D_inv = D_inv,
                    dt = dt,
                    is_fluid = is_fluid,
                    eos_stiffness = eos_stiffness,
                    eos_power = eos_power,
                    rest_density = rest_density,
                    dynamic_viscosity = dynamic_viscosity,
                    mu = mu,
                    lambda = lambda
                }.Schedule(num_par, job_size).Complete();
            }
            else
            {
                new Job_P2G()
                {
                    grid = grid,
                    ps = ps,
                    num_par = num_par,
                    i_start = i_start,
                    grid_res = grid_res,
                    i_translation = i_translation,
                    D_inv = D_inv,
                    dt = dt,
                    is_fluid = is_fluid,
                    eos_stiffness = eos_stiffness,
                    eos_power = eos_power,
                    rest_density = rest_density,
                    dynamic_viscosity = dynamic_viscosity,
                    mu = mu,
                    lambda = lambda
                }.Schedule().Complete();
            }
            stopwatch.Stop();
            timers.P2G = (stopwatch.ElapsedTicks / (float)Stopwatch.Frequency) * 1000;
            
            stopwatch = Stopwatch.StartNew();
            new Job_GridVelocityUpdate()
            {
                grid = grid,
                grid_res = grid_res,
                apply_gravity = apply_gravity,
                gravity = gravity,
                dt = dt
            }.Schedule(num_cells, job_size).Complete();
            stopwatch.Stop();
            timers.GridVelocityUpdate = (stopwatch.ElapsedTicks / (float)Stopwatch.Frequency) * 1000;
            
            stopwatch = Stopwatch.StartNew();
            new Job_G2P()
            {
                ps = ps,
                grid = grid,
                i_start = i_start,
                i_translation = i_translation,
                D_inv = D_inv,
                grid_res = grid_res,
                dt = dt,
                mouse_down = mouse_down,
                mouse_position = mouse_position
            }.Schedule(num_par, job_size).Complete();
            stopwatch.Stop();
            timers.G2P = (stopwatch.ElapsedTicks / (float)Stopwatch.Frequency) * 1000;

            stopwatch = Stopwatch.StartNew();
            new Job_ResetGrid()
            {
                grid = grid
            }.Schedule(num_cells, job_size).Complete();
            stopwatch.Stop();
            timers.ResetGrid = (stopwatch.ElapsedTicks / (float)Stopwatch.Frequency) * 1000;

            timers.Total = timers.P2G + timers.GridVelocityUpdate + timers.G2P + timers.ResetGrid;
        }

        #endregion

        #region Render
        public void GetParticles(ComputeBuffer target)
        {
            new Job_GetParticles()
            {
                ps = ps,
                source = source
            }.Schedule(num_par, job_size).Complete();

            target.SetData(source);
        }

        #endregion

        #region GUI

        void OnGUI()
        {
            if (show_info)
            {
                for (int i = 0; i < 2; i++)
                {
                    float width = 185, height = 20, spacing = 15;
                    Rect rect = i == 0 ? new Rect(1, 1, width, height) : new Rect(0, 0, width, height);
                    //GUI.Box(rect, "");
                    GUI.color = i == 0 ? Color.grey : Color.white;
                    GUI.Label(rect, string.Format("Number of Particles \t{0}", num_par)); rect.y += spacing;
                    GUI.Label(rect, string.Format("Number of Cells     \t{0}", num_cells)); rect.y += spacing;
                }
            }

            if (show_timers)
            {
                for (int i = 0; i < 2; i++)
                {
                    float width = 185, height = 20, spacing = 15;
                    Rect rect = i == 0 ? new Rect(Screen.width - width + 1, 1, width, height) : new Rect(Screen.width - width, 0, width, height);
                    //GUI.Box(rect, "");
                    GUI.color = i == 0 ? Color.grey : Color.white;
                    GUI.Label(rect, string.Format("P2G                \t{0:F2} ms", timers.P2G)); rect.y += spacing;
                    GUI.Label(rect, string.Format("GridVelocityUpdate \t{0:F2} ms", timers.GridVelocityUpdate)); rect.y += spacing;
                    GUI.Label(rect, string.Format("G2P                \t{0:F2} ms", timers.G2P)); rect.y += spacing;
                    GUI.Label(rect, string.Format("ResetGrid          \t{0:F2} ms", timers.ResetGrid)); rect.y += spacing;
                    GUI.Label(rect, string.Format("Total              \t{0:F2} ms", timers.Total)); rect.y += spacing;
                }
            }
        }

        #endregion

        #region Interpolation

        [BurstCompile]
        static float GetWeight(float3 dist)
        {
            if (interpolation == Interpolation.Cubic)
            {
                return Cubic(dist[0]) * Cubic(dist[1]) * Cubic(dist[2]);
            }
            else
            {
                return Quadratic(dist[0]) * Quadratic(dist[1]) * Quadratic(dist[2]);
            }
        }

        [BurstCompile]
        static float Cubic(float x)
        {
            float w;
            x = math.abs(x);

            if (x < 1)
            {
                w = (x * x * x / 2.0f - x * x + 2.0f / 3.0f);
            }
            else if (x < 2)
            {
                w = (2.0f - x) * (2.0f - x) * (2.0f - x) / 6.0f;
            }
            else
            {
                w = 0f;
            }

            return w;
        }

        [BurstCompile]
        static float Quadratic(float x)
        {
            float w;
            x = math.abs(x);

            if (x < 0.5f)
            {
                w = -x * x + 3.0f / 4.0f;
            }
            else if (x < 1.5f)
            {
                w = x * x / 2.0f - 3.0f * x / 2.0f + 9.0f / 8.0f;
            }
            else
            {
                w = 0f;
            }

            return w;
        }

        #endregion

        [BurstCompile]
        public static float AtomicAdd(ref float location1, float value)
        {
            var newCurrentValue = location1;
            while (true)
            {
                var currentValue = newCurrentValue;
                var newValue = currentValue + value;
                newCurrentValue = Interlocked.CompareExchange(ref location1, newValue, currentValue);
                if (newCurrentValue == currentValue)
                    return newValue;
            }
        }

        #region Jobs
        [BurstCompile]
        struct Job_GetParticles : IJobParallelFor
        {
            public NativeArray<Particle> ps;
            public NativeArray<float4> source;

            public void Execute(int i)
            {
                source[i] = new float4(ps[i].x.x, ps[i].x.y, ps[i].x.z, 1.0f);
            }
        }

        [BurstCompile]
        struct Job_ResetGrid : IJobParallelFor
        {
            public NativeArray<Cell> grid;
            public void Execute(int i)
            {
                var c = grid[i];

                c.v = float3.zero;
                c.m = 0.0f;

                grid[i] = c;
            }
        }

        [BurstCompile]
        struct Job_P2G : IJob
        {
            public NativeArray<Cell> grid;
            [ReadOnly] public NativeArray<Particle> ps;
            [ReadOnly] public int num_par;
            [ReadOnly] public int i_start;
            [ReadOnly] public int grid_res;
            [ReadOnly] public float i_translation;
            [ReadOnly] public float D_inv;
            [ReadOnly] public float dt;
            [ReadOnly] public bool is_fluid;
            [ReadOnly] public float eos_stiffness;
            [ReadOnly] public float eos_power;
            [ReadOnly] public float rest_density;
            [ReadOnly] public float dynamic_viscosity;
            [ReadOnly] public float mu;
            [ReadOnly] public float lambda;

            public void Execute()
            {
                for (int i = 0; i < num_par; i++)
                {
                    var p = ps[i];

                    float volume = p.volume_0 * math.determinant(p.F);

                    float3x3 stress;

                    if (is_fluid)
                    {
                        float density = p.m / volume;
                        volume = math.clamp(volume, 0.6f, 100.0f);

                        //float pressure = math.max(-0.1f, eos_stiffness * (math.pow(density / rest_density, eos_power) - 1));
                        //float pressure = eos_stiffness * (math.pow(density / rest_density, eos_power) - 1);
                        float pressure = eos_stiffness * (density - rest_density);    
                        stress = -pressure * float3x3.identity;
                        float3x3 strain = p.C + math.transpose(p.C);
                        float3x3 viscosity = dynamic_viscosity * strain;
                        stress += viscosity;
                    }

                    else
                    {
                        var F = p.F;
                        var J = math.determinant(F);

                        var F_T = math.transpose(F);
                        var F_inv_T = math.inverse(F_T);

                        var P_term_0 = mu * (F - F_inv_T);
                        var P_term_1 = lambda * math.log(J) * F_inv_T;
                        var P = P_term_0 + P_term_1;
                        stress = (1.0f / J) * math.mul(P, F_T);
                    }

                    float3x3 term = -volume * D_inv * stress * dt;
                    for (int gx = i_start; gx < 3; gx++)
                    {
                        for (int gy = i_start; gy < 3; gy++)
                        {
                            for (int gz = i_start; gz < 3; gz++)
                            {
                                uint3 cell = new uint3
                                (
                                    (uint)(p.x.x + gx - i_translation),
                                    (uint)(p.x.y + gy - i_translation),
                                    (uint)(p.x.z + gz - i_translation)
                                );
                                int cell_idx = ((int)cell.z * grid_res * grid_res) + ((int)cell.y * grid_res) + (int)cell.x;

                                //float3 cell_dist = (cell - p.x);
                                float3 cell_dist = (p.x - cell);
                                float w = GetWeight(cell_dist);
                                float3 Q = math.mul(p.C, cell_dist);

                                float mass_contrib = w * p.m;

                                Cell c = grid[cell_idx];
                                
                                c.m += mass_contrib;
                                c.v += mass_contrib * (p.v + Q);

                                float3 momentum = math.mul(term * w, cell_dist);
                                c.v += momentum;

                                grid[cell_idx] = c;
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct Job_P2G_MT : IJobParallelFor
        {
            public NativeArray<Cell> grid;
            [ReadOnly] public NativeArray<Particle> ps;
            [ReadOnly] public int num_par;
            [ReadOnly] public int i_start;
            [ReadOnly] public int grid_res;
            [ReadOnly] public float i_translation;
            [ReadOnly] public float D_inv;
            [ReadOnly] public float dt;
            [ReadOnly] public bool is_fluid;
            [ReadOnly] public float eos_stiffness;
            [ReadOnly] public float eos_power;
            [ReadOnly] public float rest_density;
            [ReadOnly] public float dynamic_viscosity;
            [ReadOnly] public float mu;
            [ReadOnly] public float lambda;

            public void Execute(int i)
            {
                var p = ps[i];

                float volume = p.volume_0 * math.determinant(p.F);

                float3x3 stress;

                if (is_fluid)
                {
                    float density = p.m / volume;
                    volume = math.clamp(volume, 0.6f, 100.0f);

                    //float pressure = math.max(-0.1f, eos_stiffness * (math.pow(density / rest_density, eos_power) - 1));
                    //float pressure = eos_stiffness * (math.pow(density / rest_density, eos_power) - 1);
                    float pressure = eos_stiffness * (density - rest_density);
                    stress = -pressure * float3x3.identity;
                    float3x3 strain = p.C + math.transpose(p.C);
                    float3x3 viscosity = dynamic_viscosity * strain;
                    stress += viscosity;
                }

                else
                {
                    var F = p.F;
                    var J = math.determinant(F);

                    var F_T = math.transpose(F);
                    var F_inv_T = math.inverse(F_T);

                    var P_term_0 = mu * (F - F_inv_T);
                    var P_term_1 = lambda * math.log(J) * F_inv_T;
                    var P = P_term_0 + P_term_1;
                    stress = (1.0f / J) * math.mul(P, F_T);
                }

                float3x3 term = -volume * D_inv * stress * dt;
                for (int gx = i_start; gx < 3; gx++)
                {
                    for (int gy = i_start; gy < 3; gy++)
                    {
                        for (int gz = i_start; gz < 3; gz++)
                        {
                            uint3 cell = new uint3
                            (
                                (uint)(p.x.x + gx - i_translation),
                                (uint)(p.x.y + gy - i_translation),
                                (uint)(p.x.z + gz - i_translation)
                            );
                            int cell_idx = ((int)cell.z * grid_res * grid_res) + ((int)cell.y * grid_res) + (int)cell.x;

                            //float3 cell_dist = (cell - p.x);
                            float3 cell_dist = (p.x - cell);
                            float w = GetWeight(cell_dist);
                            float3 Q = math.mul(p.C, cell_dist);

                            float mass_contrib = w * p.m;
                            float3 tmp = mass_contrib * (p.v + Q);

                            float3 momentum = math.mul(term * w, cell_dist) + tmp;
                            unsafe
                            {
                                var ptr = (Cell*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(grid);

                                var t = AtomicAdd(ref ptr[cell_idx].m, mass_contrib);
                                t = AtomicAdd(ref ptr[cell_idx].v.x, momentum.x);
                                t = AtomicAdd(ref ptr[cell_idx].v.y, momentum.y);
                                t = AtomicAdd(ref ptr[cell_idx].v.z, momentum.z);
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct Job_GridVelocityUpdate : IJobParallelFor
        {
            public NativeArray<Cell> grid;
            [ReadOnly] public int grid_res;
            [ReadOnly] public bool apply_gravity;
            [ReadOnly] public float3 gravity;
            [ReadOnly] public float dt;

            public void Execute(int i)
            {
                var c = grid[i];

                if (c.m > 0)
                {
                    c.v /= c.m;

                    if (apply_gravity)
                    {
                        c.v += dt * gravity;
                    }

                    int z = i / (grid_res * grid_res);
                    int j = i - (z * grid_res * grid_res);
                    int y = j / grid_res;
                    int x = j % grid_res;

                    // Apply Boundary conditions
                    if (x < 2 || x > grid_res - 2) { c.v = 0; }
                    if (y < 2 || y > grid_res - 2) { c.v.y = 0; }
                    if (z < 2 || z > grid_res - 2) { c.v = 0; }

                    grid[i] = c;
                }
            }
        }

        [BurstCompile]
        struct Job_G2P : IJobParallelFor
        {
            public NativeArray<Particle> ps;
            [ReadOnly] public NativeArray<Cell> grid;
            [ReadOnly] public int i_start;
            [ReadOnly] public float i_translation;
            [ReadOnly] public float D_inv;
            [ReadOnly] public int grid_res;
            [ReadOnly] public float dt;
            [ReadOnly] public bool mouse_down;
            [ReadOnly] public float3 mouse_position;

            public void Execute(int i)
            {
                var p = ps[i];

                p.v = float3.zero;

                float3x3 B = 0;
                for (int gx = i_start; gx < 3; gx++)
                {
                    for (int gy = i_start; gy < 3; gy++)
                    {
                        for (int gz = i_start; gz < 3; gz++)
                        {
                            uint3 cell = new uint3
                            (
                                (uint)(p.x.x + gx - i_translation),
                                (uint)(p.x.y + gy - i_translation),
                                (uint)(p.x.z + gz - i_translation)
                            );
                            int cell_idx = ((int)cell.z * grid_res * grid_res) + ((int)cell.y * grid_res) + (int)cell.x;

                            //float3 cell_dist = (cell - p.x);
                            float3 cell_dist = (p.x - cell);
                            float w = GetWeight(cell_dist);
                            float3 v_contrib = grid[cell_idx].v * w;

                            // Outer product
                            float3x3 term = new float3x3(v_contrib * cell_dist.x, v_contrib * cell_dist.y, v_contrib * cell_dist.z);

                            B += term;
                            p.v += v_contrib;
                        }
                    }
                }
                p.C = B * D_inv;
                p.F = math.mul(float3x3.identity + dt * p.C, p.F);

                if (mouse_down)
                {
                    var dist = p.x - mouse_position;
                    if (math.dot(dist, dist) < mouse_radius * mouse_radius)
                    {
                        float norm_factor = (math.length(dist) / mouse_radius);
                        norm_factor = math.pow(math.sqrt(norm_factor), 8);
                        var force = math.normalize(dist) * norm_factor * 0.5f;
                        p.v += force;
                    }
                }

                p.x += dt * p.v;
                // Clamp particle positions to ensure that they don't leave the simulation domain
                p.x = math.clamp(p.x, 1, grid_res - 2);

                ps[i] = p;
            }
        }

        #endregion

        private void OnDestroy()
        {
            ps.Dispose();
            grid.Dispose();
            source.Dispose();
        }
    }
}