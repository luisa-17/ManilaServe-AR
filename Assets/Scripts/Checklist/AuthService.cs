    using System.Threading.Tasks;
    using Firebase;
    using Firebase.Auth;
    using UnityEngine;

    public static class AuthService
    {
        static bool _initialized;
        static FirebaseAuth _auth;

        public static bool IsSignedIn => _initialized && _auth != null && _auth.CurrentUser != null;
        public static string UserId => _auth?.CurrentUser?.UserId;
        public static bool IsAnonymous => _auth?.CurrentUser?.IsAnonymous ?? false;

        public static async Task<bool> EnsureInitializedAsync()
        {
            if (_initialized) return true;

            var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dep != DependencyStatus.Available)
            {
                Debug.LogError("Firebase dependencies not available: " + dep);
                return false;
            }

            _auth = FirebaseAuth.DefaultInstance;

            // NOTE: Some Firebase Unity versions don�t expose SetPersistenceAsync/Persistence in C#.
            // We�ll rely on the default persistence and skip explicit calls to SetPersistenceAsync.

            _initialized = true;
            return true;
        }

        public static async Task<(bool ok, string message)> SignInEmailPasswordAsync(string email, string password)
        {
            if (!await EnsureInitializedAsync()) return (false, "Firebase not initialized");

            try
            {
                await _auth.SignInWithEmailAndPasswordAsync(email, password);
                return (true, "Signed in");
            }
            catch (System.Exception e)
            {
                return (false, e.Message);
            }
        }

        public static async Task<(bool ok, string message)> SignUpEmailPasswordAsync(string email, string password, string displayName = null)
        {
            if (!await EnsureInitializedAsync()) return (false, "Firebase not initialized");

            try
            {
                var cred = await _auth.CreateUserWithEmailAndPasswordAsync(email, password);
                if (!string.IsNullOrEmpty(displayName))
                {
                    var profile = new UserProfile { DisplayName = displayName };
                    await cred.User.UpdateUserProfileAsync(profile);
                }
                return (true, "Account created");
            }
            catch (System.Exception e)
            {
                return (false, e.Message);
            }
        }

        public static async Task<(bool ok, string message)> SignInAnonymouslyAsync()
        {
            if (!await EnsureInitializedAsync()) return (false, "Firebase not initialized");

            try
            {
                await _auth.SignInAnonymouslyAsync();
                return (true, "Signed in as guest");
            }
            catch (System.Exception e)
            {
                return (false, e.Message);
            }
        }

        public static void SignOut()
        {
            if (!_initialized || _auth == null) return;
            _auth.SignOut();
        }
    }