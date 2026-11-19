namespace HuaweiARUnitySDK
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using HuaweiARInternal;

    /**
     * \if english
     * @brief Helper methods for dealing with asynchronous tasks.
     * \else
     * @brief 帮助处理异步任务的类。
     * \endif
     */
    public class AsyncTask
    {
        private static Queue<Action> actionQueue = new Queue<Action>();

        private static object for_lock = new object();

        /**
         * \if english
         * @brief Queues an action to be performed in Update(). This method can be called by any thread.
         * @param action The action to perform.
         * \else
         * @brief 添加一个在Update()函数中执行的任务。该方法可以在任意线程中调用。
         * @param action 要执行的action.
         * \endif
         */
        public static void AddTask(Action action)
        {
            lock (for_lock)
            {
                actionQueue.Enqueue(action);
            }
        }

        /**
         * \if english
         * @brief An Update handler should called each frame in unity 
         * <a href="https://docs.unity3d.com/ScriptReference/MonoBehaviour.Update.html">Update()</a>.
         * \else
         * @brief 执行所有添加的任务，该方法应该在unity的
         * <a href="https://docs.unity3d.com/ScriptReference/MonoBehaviour.Update.html">Update()</a>
         * 函数中使用。
         * \endif
         */
        public static void Update()
        {
            lock (for_lock)
            {
                while (actionQueue.Count > 0)
                {
                    actionQueue.Dequeue().Invoke();
                }
            }
        }
    }
}
