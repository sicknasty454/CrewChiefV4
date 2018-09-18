﻿/*
 * This class' responsibility is to provide some corrdination between starting/stopping threads in CC.  Here's proposal for strategy of
 * dealing with threads in CC, aka "Architecture":
 * 
 * 1. each root thread (the one we start from MainWindow/UI thread) has to be registered with ThreadManager and named
 * 
 * 1.1: FUTURE: each temporary Thread should also be registered
 * 
 * 2. if any of the root thread creates a long lived thread, it has to wait for it to exit when root thread exits.
 * 
 * 3. each thread should release/dispose it's resources on exit, unless it is too complicated/unpractical (See GlobalResources class)
 * 
 * 4. global shared resources will be released after form close (and all root threads stopped, if they stop within agreed time, otherwise - undefined behavior).
 * 
 * 5. access to main window should be synchronized with lock.  Be extra careful, if you are marshalling to the main thread, do a Post, not Send, so that lock is not held
 *    Failing to follow above might cause deadlocks.
 * 
 * 6. For Sleeps consider using Utilities.InterruptedSleep to avoid long shutdown delays.
 * 
 * 7. Work worker threads that pump some data, don't just use Sleep, use Events to wake them up.
 * 
 * Future: unsolved problems
 *  - Download threasds
 *  - File dump in main run thread
 *
 * Official website: thecrewchief.org 
 * License: MIT
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CrewChiefV4
{
    public static class ThreadManager
    {
        private const int THREAD_ALIVE_CHECK_PERIOD_MILLIS = 200;
        private const int THREAD_ALIVE_TOTAL_WAIT_SECS = 5;
        private const int THREAD_ALIVE_WAIT_ITERATIONS = ThreadManager.THREAD_ALIVE_TOTAL_WAIT_SECS * 1000 / ThreadManager.THREAD_ALIVE_CHECK_PERIOD_MILLIS;

        private const int SHUTDOWN_THREAD_ALIVE_CHECK_PERIOD_MILLIS = 50;
        private const int SHUTDOWN_THREAD_ALIVE_TOTAL_WAIT_SECS = 5;
        private const int SHUTDOWN_THREAD_ALIVE_WAIT_ITERATIONS = ThreadManager.SHUTDOWN_THREAD_ALIVE_TOTAL_WAIT_SECS * 1000 / ThreadManager.SHUTDOWN_THREAD_ALIVE_CHECK_PERIOD_MILLIS;

        private static List<Thread> rootThreads = new List<Thread>();

        // TODO_THREADS: implement temporary thread registration/wait.

        public static void RegisterRootThread(Thread t)
        {
            lock (MainWindow.instanceLock)
            {
                if (MainWindow.instance != null
                    && MainWindow.instance.InvokeRequired)
                {
                    Debug.Assert(false, "This method is supposed to be invoked only from the UI thread.");
                    return;
                }
            }

            lock (ThreadManager.rootThreads)
            {
                ThreadManager.rootThreads.Add(t);
            }
        }

        private static void UnregisterRootThreads()
        {
            lock (ThreadManager.rootThreads)
            {
                ThreadManager.rootThreads.Clear();
            }
        }

        public static void DoWatchStartup(CrewChief cc)
        {
            new Thread(() =>
            {
                ThreadManager.WaitForRootThreadsStart(cc);
            }).Start();
        }

        public static void DoWatchStop(CrewChief cc)
        {
            new Thread(() =>
            {
                ThreadManager.WaitForRootThreadsStop(cc);
            }).Start();
        }

        // This is not strictly necessary, because all this really does is makes sure .Start has been called on a thread, which is easy
        // to achieve.  Still, do this for symmetry.
        public static bool WaitForRootThreadsStart(CrewChief cc)
        {
            try
            {
                lock (MainWindow.instanceLock)
                {
                    if (MainWindow.instance != null
                       && !MainWindow.instance.InvokeRequired)
                    {
                        Debug.Assert(false, "This method cannot be invoked from the UI thread.");
                        return false;
                    }
                }

                ThreadManager.Trace("Wating for root threads to start...");
                for (int i = 0; i < ThreadManager.THREAD_ALIVE_WAIT_ITERATIONS; ++i)
                {
                    var allThreadsRunning = true;
                    lock (ThreadManager.rootThreads)
                    {
                        foreach (var t in ThreadManager.rootThreads)
                        {
                            if (!t.IsAlive)
                            {
                                ThreadManager.Trace("Thread not running - " + t.Name);
                                allThreadsRunning = false;
                                break;
                            }
                        }
                    }

                    if (allThreadsRunning)
                    {
                        ThreadManager.Trace("Root threads started");
                        var isTraceFileSet = false;
                        lock (MainWindow.instanceLock)
                        {
                            isTraceFileSet = MainWindow.instance != null && !string.IsNullOrWhiteSpace(MainWindow.instance.filenameTextbox.Text);
                        }

                        if (isTraceFileSet)
                        {
                            ThreadManager.Trace("Wating for run thread to read data file...");
                            while (true)
                            {
                                if (cc.dataFileReadDone)
                                {
                                    ThreadManager.Trace("Run thread data file read done");
                                    break;
                                }

                                Thread.Sleep(ThreadManager.THREAD_ALIVE_CHECK_PERIOD_MILLIS);
                            }
                        }

                        return true;
                    }

                    Thread.Sleep(ThreadManager.THREAD_ALIVE_CHECK_PERIOD_MILLIS);
                }

                ThreadManager.Trace("Wait for root threads start failed:");
                ThreadManager.TraceRootThreadStats();

                return false;
            }
            finally
            {
                lock (MainWindow.instanceLock)
                {
                    if (MainWindow.instance != null)
                    {
                        // Not entirely sure if Invoke is necessary here, seems to work well without it.  If we decide
                        // invoke is needed, Post might be best option here.
                        MainWindow.instance.startApplicationButton.Enabled = true;
                    }
                }
            }
        }

        public static bool WaitForRootThreadsStop(CrewChief cc)
        {
            try
            {
                lock (MainWindow.instanceLock)
                {
                    if (MainWindow.instance != null
                        && !MainWindow.instance.InvokeRequired)
                    {
                        Debug.Assert(false, "This method cannot be invoked from the UI thread.");
                        return false;
                    }
                }

                var recordSessionChecked = false;
                lock (MainWindow.instanceLock)
                {
                    recordSessionChecked = MainWindow.instance != null && MainWindow.instance.recordSession.Checked;
                }

                if (recordSessionChecked)
                {
                    ThreadManager.Trace("Wating for run thread to dump data file...");
                    while (true)
                    {
                        if (cc.dataFileDumpDone)
                        {
                            ThreadManager.Trace("Run thread data file dump done");
                            break;
                        }

                        Thread.Sleep(ThreadManager.THREAD_ALIVE_CHECK_PERIOD_MILLIS);
                    }
                }

                ThreadManager.Trace("Wating for root threads to stop...");
                for (int i = 0; i < ThreadManager.THREAD_ALIVE_WAIT_ITERATIONS; ++i)
                {
                    var allThreadsStopped = true;
                    lock (ThreadManager.rootThreads)
                    {
                        foreach (var t in ThreadManager.rootThreads)
                        {
                            if (t.IsAlive)
                            {
                                ThreadManager.Trace("Thread still alive - " + t.Name);
                                allThreadsStopped = false;
                                break;
                            }
                        }
                    }

                    if (allThreadsStopped)
                    {
                        ThreadManager.Trace("Root threads stopped");
                        return true;
                    }
                    
                    Thread.Sleep(ThreadManager.THREAD_ALIVE_CHECK_PERIOD_MILLIS);
                }

                ThreadManager.Trace("Wait for root threads stop failed:");
                ThreadManager.TraceRootThreadStats();

                return false;
            }
            finally
            {
                lock (MainWindow.instanceLock)
                {
                    if (MainWindow.instance != null)
                    {
                        ThreadManager.UnregisterRootThreads();
                        MainWindow.instance.startApplicationButton.Enabled = true;
                    }
                }
            }
        }

        // Note: wait for file dump on shutdown is not supported.
        public static bool WaitForRootThreadsShutdown()
        {
            if (ThreadManager.rootThreads.Count == 0)
                return true;

            // Possibly, print to debug log?
            Debug.WriteLine("Shutdown: Wating for root threads to stop...");
            for (int i = 0; i < ThreadManager.SHUTDOWN_THREAD_ALIVE_WAIT_ITERATIONS; ++i)
            {
                var allThreadsStopped = true;
                lock (ThreadManager.rootThreads)
                {
                    foreach (var t in ThreadManager.rootThreads)
                    {
                        if (t.IsAlive)
                        {
                            Debug.WriteLine("Shutdown: Thread still alive - " + t.Name);
                            allThreadsStopped = false;
                            break;
                        }
                    }
                }

                if (allThreadsStopped)
                {
                    Debug.WriteLine("Shutdown: Root threads stopped");
                    return true;
                }

                Thread.Sleep(ThreadManager.SHUTDOWN_THREAD_ALIVE_CHECK_PERIOD_MILLIS);
            }

            
            Debug.WriteLine("Shutdown: Wait for root threads stop failed, thread states:");
            ThreadManager.DebugTraceRootThreadStats();

            // Note: wait for file dump on shutdown is not supported, if this assert annoys you, remove it.
            // Alternatively, change this code to wait for dump to finish?
            Debug.Assert(false, "Shutdown: Wait for root threads stop failed, please investigate.");

            return false;
        }

        private static void TraceRootThreadStats()
        {
            // If we run into bad problems, we might also need to get stack trace out.
            lock (ThreadManager.rootThreads)
            {
                foreach (var t in ThreadManager.rootThreads)
                    ThreadManager.Trace(string.Format("Thread Name: {0}  ThreadState: {1}  IsAlive: {2}", t.Name, t.ThreadState, t.IsAlive));
            }
        }

        private static void DebugTraceRootThreadStats()
        {
            // If we run into bad problems, we might also need to get stack trace out.
            lock (ThreadManager.rootThreads)
            {
                foreach (var t in ThreadManager.rootThreads)
                    Debug.WriteLine(string.Format("Thread Name: {0}  ThreadState: {1}  IsAlive: {2}", t.Name, t.ThreadState, t.IsAlive));
            }
        }

        private static void Trace(string msg)
        {
            Console.WriteLine(string.Format("ThreadManager: {0}", msg));
        }
    }
}
