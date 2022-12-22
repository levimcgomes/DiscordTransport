using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace levimcgomes.Utils
{
    /// <summary>
    /// Implements a singleton pattern generically, for any UnityEngine.Component. Inherit from this class to use.
    /// </summary>
    /// <typeparam name="T">The type of the singleton instance. It should be the same as the type inheriting from this class.</typeparam>
    /// <example>
    /// public class Example : Singleton<Example>
    /// </example>
    public class Singleton<T> : MonoBehaviour
        where T : Component
    {
        private static T _instance;
        public static T Instance {
            get {
                if(_instance == null) {
                    var objs = FindObjectsOfType(typeof(T)) as T[];
                    if(objs.Length > 0)
                        _instance = objs[0];
                    if(objs.Length > 1) {
                        Debug.LogError("There is more than one " + typeof(T).Name + " in the scene.");
                    }
                    if(_instance == null) {
                        GameObject obj = new GameObject();
                        obj.name = string.Format("_{0}", typeof(T).Name);
                        _instance = obj.AddComponent<T>();
                    }
                }
                return _instance;
            }
        }
    } 
}
