using System;
using System.Linq;
using System.Reflection;

namespace ArrowFixes
{
    /// <summary>
    /// TheForest.Utils.Input takes action names as strings in the flat-screen build and as Input.Actions.* objects in
    /// the VR build. Resolve the right overload once by name.
    /// </summary>
    internal static class InputCompat
    {
        public static Func<bool> Button(string action) => Build("GetButton", action);

        public static Func<bool> ButtonDown(string action) => Build("GetButtonDown", action);

        private static Func<bool> Build(string methodName, string action)
        {
            var input = typeof(TheForest.Utils.Input);
            var method = input.GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 1)
                         ?? throw new MissingMethodException(input.FullName, methodName);
            var paramType = method.GetParameters()[0].ParameterType;
            object arg;
            if (paramType == typeof(string))
            {
                arg = action;
            }
            else
            {
                var actions = input.GetNestedType("Actions", BindingFlags.Public | BindingFlags.NonPublic)
                              ?? throw new MissingMemberException(input.FullName, "Actions");
                arg = actions.GetField(action, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                      ?? throw new MissingMemberException(actions.FullName, action);
            }
            var args = new[] { arg };
            return () => (bool)method.Invoke(null, args);
        }
    }
}
