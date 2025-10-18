using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

public static class AuthService
{
    static bool _initialized;
    static FirebaseAuth _auth;

    public static bool IsInitialized => _initialized;
    public static FirebaseAuth Auth => _auth;
    public static FirebaseUser CurrentUser => _auth?.CurrentUser;

    public static bool IsSignedIn => _initialized && _auth != null && _auth.CurrentUser != null;
    public static string UserId => _auth?.CurrentUser?.UserId;
    public static bool IsAnonymous => _auth?.CurrentUser?.IsAnonymous ?? false;

    public static async Task<bool> EnsureInitializedAsync()
    {
        if (_initialized && _auth != null) return true;

        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            Debug.LogError("Firebase dependencies not available: " + dep);
            return false;
        }

        _auth = FirebaseAuth.DefaultInstance;

        // Persistence note:
        // Some Firebase Unity versions don't expose SetPersistenceAsync/Persistence in C#.
        // We'll rely on the default persistence.

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
        catch (Exception e)
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
        catch (Exception e)
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
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    // Helps avoid showing login UI while Firebase is still restoring a persisted user.
    public static async Task<bool> WaitForAuthRestorationAsync(int timeoutMs = 1500)
    {
        if (!await EnsureInitializedAsync()) return false;
        if (_auth.CurrentUser != null) return true;

        var tcs = new TaskCompletionSource<bool>();

        void Handler(object s, EventArgs e)
        {
            if (_auth.CurrentUser != null) tcs.TrySetResult(true);
        }

        _auth.StateChanged += Handler;
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        }
        finally
        {
            _auth.StateChanged -= Handler;
        }

        return _auth.CurrentUser != null;
    }

    public static void SignOut()
    {
        try
        {
            if (_initialized && _auth != null)
            {
                _auth.SignOut();
            }
            else
            {
                // Fallback if called before EnsureInitializedAsync
                FirebaseAuth.DefaultInstance?.SignOut();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"AuthService.SignOut exception: {e.Message}");
        }
    }
}