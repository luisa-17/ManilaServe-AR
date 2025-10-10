using UnityEngine;

public class UserAuthManager : MonoBehaviour
{
    public bool IsUserLoggedIn()
    {
        return AuthService.IsSignedIn;
    }

    public string GetCurrentUserId()
    {
        return AuthService.UserId;
    }
}
