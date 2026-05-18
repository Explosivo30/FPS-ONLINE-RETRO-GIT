using System.Collections.Generic;

namespace EZCollabTool
{
    public class EZCollabLockManager
    {
        readonly Dictionary<string, string> locks = new Dictionary<string, string>();

        public bool TryAcquire(string guid, string peerId)
        {
            if (locks.TryGetValue(guid, out string current))
                return current == peerId;

            locks[guid] = peerId;
            return true;
        }

        public void Release(string guid, string peerId)
        {
            if (locks.TryGetValue(guid, out string current) && current == peerId)
                locks.Remove(guid);
        }

        public List<string> ReleaseAll(string peerId)
        {
            var released = new List<string>();
            var keys = new List<string>(locks.Keys);

            foreach (var key in keys)
            {
                if (locks[key] == peerId)
                {
                    released.Add(key);
                    locks.Remove(key);
                }
            }

            return released;
        }

        public bool IsLockedBy(string guid, string peerId)
        {
            return locks.TryGetValue(guid, out string current) && current == peerId;
        }

        public string GetOwner(string guid)
        {
            locks.TryGetValue(guid, out string owner);
            return owner;
        }
    }
}
