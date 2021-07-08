using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Thalitech.ABRipper
{
    public class ThreadManager
    {
        static volatile bool _queued = false;
        static List<Action> _backlog = new List<Action>(16);
        static List<Action> _actions = new List<Action>(16);

        ////////////////////////
        /// Threading
        /// ///////////////

        public static void RunAsync(Action action)
        {
            ThreadPool.QueueUserWorkItem(o => action());
        }
        public static void RunAsync(Action<object> action, object state)
        {
            ThreadPool.QueueUserWorkItem(o => action(o), state);
        }
        public static void RunOnMainThread(Action action)
        {
            lock (_backlog)
            {
                _backlog.Add(action);
                _queued = true;
            }
        }

        public static void Update()
        {
            if (_queued)
            {
                lock (_backlog)
                {
                    var tmp = _actions;
                    _actions = _backlog;
                    _backlog = tmp;
                    _queued = false;
                }
                foreach (var action in _actions) action();
                _actions.Clear();
            }
        }

        internal static void Reset()
        {
            _queued = false;
            _backlog = new List<Action>(16);
            _actions = new List<Action>(16);
        }
    }
}
