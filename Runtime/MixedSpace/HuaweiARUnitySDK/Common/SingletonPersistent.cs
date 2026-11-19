using UnityEngine;
public class SingletonPersistent<T> : MonoBehaviour where T : Component
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<T>();
                if (_instance == null)
                {
                    GameObject singletonObj = new GameObject(typeof(T).Name + " (Singleton)");
                    _instance = singletonObj.AddComponent<T>();
                }
                DontDestroyOnLoad(_instance.gameObject);
            }

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            if (_instance != this)
            {
                Debug.LogWarning($"检测到重复的 {typeof(T).Name} 实例，已自动销毁重复对象！");
                Destroy(gameObject);
            }
        }
    }
}