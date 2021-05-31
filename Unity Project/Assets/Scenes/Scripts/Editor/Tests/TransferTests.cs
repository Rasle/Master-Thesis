using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SIM;
using System.IO;
using System;
using Unity.Mathematics;

public class TransferTests
{
    [Test]
    public void PerformanceTestBurstMT()
    {
        int size = 10;
        float[] spacings = new float[] { 0.4f, 0.3f, 0.2f, 0.1f, 0.08f, 0.07f, 0.06f, 0.05f, 0.04f, 0.03f };
        int[] grid_sizes = new int[] { 8, 10, 12, 14, 16, 18, 20, 22, 24, 26 };
        int[] num_particles = new int[size];
        float[] timers = new float[size];

        // Cold run
        MPMBurst mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
        mpm.grid_res = grid_sizes[0];
        mpm.Setup(spacings[0], false);
        mpm.SimulateWithTimers();
        UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());
        UnityEngine.Object.DestroyImmediate(mpm);

        for (int i = 0; i < size; i++)
        {
            mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
            mpm.grid_res = grid_sizes[i];
            mpm.Setup(spacings[i], false);
            mpm.SimulateWithTimers();
            timers[i] = mpm.timers.Total;
            UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());

            num_particles[i] = mpm.num_par;
        }

        UnityEngine.Object.DestroyImmediate(mpm);

        File.WriteAllLines("Performance/Timings/num_particles_burst_mt.txt", Array.ConvertAll(num_particles, x => x.ToString()));
        File.WriteAllLines("Performance/Timings/grid_sizes_burst_mt.txt", Array.ConvertAll(grid_sizes, x => (x * x * x).ToString()));
        File.WriteAllLines("Performance/Timings/timers_burst_mt.txt", Array.ConvertAll(timers, x => x.ToString()));
    }

    [Test]
    public void PerformanceTestMT()
    {
        int size = 10;
        float[] spacings = new float[] { 0.4f, 0.3f, 0.2f, 0.1f, 0.08f, 0.07f, 0.06f, 0.05f, 0.04f, 0.03f };
        int[] grid_sizes = new int[] { 8, 10, 12, 14, 16, 18, 20, 22, 24, 26 };
        int[] num_particles = new int[size];
        float[] timers = new float[size];

        // Cold run
        MPMBurst mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
        mpm.grid_res = grid_sizes[0];
        mpm.Setup(spacings[0], false);
        mpm.SimulateWithTimers();
        UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());
        UnityEngine.Object.DestroyImmediate(mpm);

        for (int i = 0; i < size; i++)
        {
            mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
            mpm.grid_res = grid_sizes[i];
            mpm.Setup(spacings[i], false);
            mpm.SimulateWithTimers();
            timers[i] = mpm.timers.Total;
            UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());

            num_particles[i] = mpm.num_par;
        }

        UnityEngine.Object.DestroyImmediate(mpm);

        File.WriteAllLines("Performance/Timings/num_particles_mt.txt", Array.ConvertAll(num_particles, x => x.ToString()));
        File.WriteAllLines("Performance/Timings/grid_sizes_mt.txt", Array.ConvertAll(grid_sizes, x => (x * x * x).ToString()));
        File.WriteAllLines("Performance/Timings/timers_mt.txt", Array.ConvertAll(timers, x => x.ToString()));
    }

    [Test]
    public void PerformanceTest()
    {
        int size = 10;
        float[] spacings = new float[] { 0.4f, 0.3f, 0.2f, 0.1f, 0.08f, 0.07f, 0.06f, 0.05f, 0.04f, 0.03f };
        int[] grid_sizes = new int[] { 8, 10, 12, 14, 16, 18, 20, 22, 24, 26 };
        int[] num_particles = new int[size];
        float[] timers = new float[size];

        // Cold run
        MPMBurst mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
        mpm.grid_res = grid_sizes[0];
        mpm.Setup(spacings[0], false);
        mpm.SimulateWithTimers();
        UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());
        UnityEngine.Object.DestroyImmediate(mpm);

        for (int i = 0; i < size; i++)
        {
            mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
            mpm.grid_res = grid_sizes[i];
            mpm.num_threads = 1;
            mpm.Setup(spacings[i], false);
            mpm.SimulateWithTimers();
            timers[i] = mpm.timers.Total;
            UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());

            num_particles[i] = mpm.num_par;
        }

        UnityEngine.Object.DestroyImmediate(mpm);

        File.WriteAllLines("Performance/Timings/num_particles.txt", Array.ConvertAll(num_particles, x => x.ToString()));
        File.WriteAllLines("Performance/Timings/grid_sizes.txt", Array.ConvertAll(grid_sizes, x => (x * x * x).ToString()));
        File.WriteAllLines("Performance/Timings/timers.txt", Array.ConvertAll(timers, x => x.ToString()));
    }

    [Test]
    public void PerformanceTestBurst()
    {
        int size = 10;
        float[] spacings = new float[] { 0.4f, 0.3f, 0.2f, 0.1f, 0.08f, 0.07f, 0.06f, 0.05f, 0.04f, 0.03f };
        int[] grid_sizes = new int[] { 8, 10, 12, 14, 16, 18, 20, 22, 24, 26 };
        int[] num_particles = new int[size];
        float[] timers = new float[size];

        // Cold run
        MPMBurst mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
        mpm.grid_res = grid_sizes[0];
        mpm.Setup(spacings[0], false);
        mpm.SimulateWithTimers();
        UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());
        UnityEngine.Object.DestroyImmediate(mpm);

        for (int i = 0; i < size; i++)
        {
            mpm = new GameObject("MPMBurst").AddComponent<MPMBurst>();
            mpm.grid_res = grid_sizes[i];
            mpm.num_threads = 1;
            mpm.Setup(spacings[i], false);
            mpm.SimulateWithTimers();
            timers[i] = mpm.timers.Total;
            UnityEngine.Object.DestroyImmediate(mpm.GetComponent<MPMBurst>());

            num_particles[i] = mpm.num_par;
        }

        UnityEngine.Object.DestroyImmediate(mpm);

        File.WriteAllLines("Performance/Timings/num_particles_burst.txt", Array.ConvertAll(num_particles, x => x.ToString()));
        File.WriteAllLines("Performance/Timings/grid_sizes_burst.txt", Array.ConvertAll(grid_sizes, x => (x * x * x).ToString()));
        File.WriteAllLines("Performance/Timings/timers_burst.txt", Array.ConvertAll(timers, x => x.ToString()));
    }
}
