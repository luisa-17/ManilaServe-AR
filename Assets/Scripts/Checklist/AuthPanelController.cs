using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class AuthPanelController : MonoBehaviour
{
    [Header("Login Mode")]
    public GameObject loginModeRoot;                
    public Button switchSignupModeButton;            
    public TMP_InputField loginEmailInput;           
    public TMP_InputField loginPasswordInput;        
    public Button loginButton;                       

    [Header("Signup Mode")]
    public GameObject signupModeRoot;                
    public Button switchLoginModeButton;             
    public TMP_InputField signupEmailInput;          
    public TMP_InputField signupPasswordInput;       
    public TMP_InputField signupConfirmInput;        
    public Button signupButton;                      

    [Header("Common Actions")]
    public Button guestButton;                       
    public Button closeButton;                       
    public TextMeshProUGUI errorText;                

    [Header("Rules")]
    public int minPasswordLength = 6;
    public bool validateEmailFormat = true;

    public System.Action<bool> OnClosed; 

    CanvasGroup canvasGroup;
    enum Mode { Login, Signup }
    Mode current = Mode.Login;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        // Wire mode switches
        if (switchSignupModeButton) switchSignupModeButton.onClick.AddListener(() => SwitchMode(Mode.Signup));
        if (switchLoginModeButton) switchLoginModeButton.onClick.AddListener(() => SwitchMode(Mode.Login));

        // Wire actions
        if (loginButton) loginButton.onClick.AddListener(async () => await DoSignIn());
        if (signupButton) signupButton.onClick.AddListener(async () => await DoSignUp());
        if (guestButton) guestButton.onClick.AddListener(async () => await DoGuest());
        if (closeButton) closeButton.onClick.AddListener(() => Close(false));

        // Live validation
        if (loginEmailInput) loginEmailInput.onValueChanged.AddListener(_ => ValidateForms());
        if (loginPasswordInput) loginPasswordInput.onValueChanged.AddListener(_ => ValidateForms());
        if (signupEmailInput) signupEmailInput.onValueChanged.AddListener(_ => ValidateForms());
        if (signupPasswordInput) signupPasswordInput.onValueChanged.AddListener(_ => ValidateForms());
        if (signupConfirmInput) signupConfirmInput.onValueChanged.AddListener(_ => ValidateForms());

        SetError("");
        SwitchMode(Mode.Login);
    }

    async void OnEnable()
    {
        var ok = await AuthService.EnsureInitializedAsync();
        await AuthService.WaitForAuthRestorationAsync(1500);
        if (ok && AuthService.IsSignedIn) Close(true);
    }

    void SwitchMode(Mode m)
    {
        current = m;

        if (loginModeRoot) loginModeRoot.SetActive(current == Mode.Login);
        if (signupModeRoot) signupModeRoot.SetActive(current == Mode.Signup);

        // Optional: carry over typed email between modes
        if (current == Mode.Signup && signupEmailInput && loginEmailInput && string.IsNullOrEmpty(signupEmailInput.text))
            signupEmailInput.text = loginEmailInput.text;
        if (current == Mode.Login && loginEmailInput && signupEmailInput && string.IsNullOrEmpty(loginEmailInput.text))
            loginEmailInput.text = signupEmailInput.text;

        ValidateForms();
        FocusFirstField();
    }

    void FocusFirstField()
    {
        if (current == Mode.Login)
        {
            if (loginEmailInput) loginEmailInput.Select();
        }
        else
        {
            if (signupEmailInput) signupEmailInput.Select();
        }
    }

    void ValidateForms()
    {
        // Login: email + pass only
        bool loginEmailOk = loginEmailInput && !string.IsNullOrWhiteSpace(loginEmailInput.text) &&
                            (!validateEmailFormat || loginEmailInput.text.Contains("@"));
        bool loginPassOk = loginPasswordInput && !string.IsNullOrEmpty(loginPasswordInput.text) &&
                            loginPasswordInput.text.Length >= minPasswordLength;

        // Signup: email + pass + confirm
        bool signupEmailOk = signupEmailInput && !string.IsNullOrWhiteSpace(signupEmailInput.text) &&
                             (!validateEmailFormat || signupEmailInput.text.Contains("@"));
        bool signupPassOk = signupPasswordInput && !string.IsNullOrEmpty(signupPasswordInput.text) &&
                             signupPasswordInput.text.Length >= minPasswordLength;
        bool signupConfirmOk = signupConfirmInput && signupPasswordInput &&
                               signupConfirmInput.text == signupPasswordInput.text &&
                               signupPasswordInput.text.Length >= minPasswordLength;

        if (loginButton) loginButton.interactable = (current == Mode.Login) && loginEmailOk && loginPassOk;
        if (signupButton) signupButton.interactable = (current == Mode.Signup) && signupEmailOk && signupPassOk && signupConfirmOk;
    }

    async Task DoSignIn()
    {
        SetError("");
        if (!LoginValid()) return;

        SetBusy(true);
        var (ok, msg) = await AuthService.SignInEmailPasswordAsync(
            loginEmailInput.text.Trim(), loginPasswordInput.text
        );
        SetBusy(false);

        if (ok) Close(true);
        else SetError(msg);
    }

    async Task DoSignUp()
    {
        SetError("");

        if (!SignupValid())
        {
            ValidateForms(); // ?? ensure button interactivity syncs back
            return;
        }

        SetBusy(true);

        var (ok, msg) = await AuthService.SignUpEmailPasswordAsync(
            signupEmailInput.text.Trim(),
            signupPasswordInput.text,
            null
        );

        SetBusy(false);

        if (ok)
        {
            Close(true);
        }
        else
        {
            SetError(msg);

            // ?? Make sure the UI is usable again even after error
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            ValidateForms();
        }
    }


    async Task DoGuest()
    {
        SetError("");
        SetBusy(true);
        var (ok, msg) = await AuthService.SignInAnonymouslyAsync();
        SetBusy(false);

        if (ok) Close(true);
        else SetError(msg);
    }

    bool LoginValid()
    {
        if (!loginEmailInput || !loginPasswordInput) { SetError("Missing login inputs"); return false; }
        if (string.IsNullOrWhiteSpace(loginEmailInput.text) || (validateEmailFormat && !loginEmailInput.text.Contains("@")))
        {
            SetError("Enter a valid email");
            return false;
        }
        if (string.IsNullOrEmpty(loginPasswordInput.text) || loginPasswordInput.text.Length < minPasswordLength)
        {
            SetError($"Password must be at least {minPasswordLength} characters");
            return false;
        }
        return true;
    }

    bool SignupValid()
    {
        if (!signupEmailInput || !signupPasswordInput || !signupConfirmInput) { SetError("Missing signup inputs"); return false; }
        if (string.IsNullOrWhiteSpace(signupEmailInput.text) || (validateEmailFormat && !signupEmailInput.text.Contains("@")))
        {
            SetError("Enter a valid email");
            return false;
        }
        if (string.IsNullOrEmpty(signupPasswordInput.text) || signupPasswordInput.text.Length < minPasswordLength)
        {
            SetError($"Password must be at least {minPasswordLength} characters");
            return false;
        }
        if (signupConfirmInput.text != signupPasswordInput.text)
        {
            SetError("Passwords do not match");
            return false;
        }
        return true;
    }

    void SetBusy(bool busy)
    {
        canvasGroup.interactable = !busy;
        canvasGroup.blocksRaycasts = !busy;
    }

    void SetError(string message)
    {
        if (errorText) errorText.text = message ?? "";
    }

    public void Close(bool success)
    {
        gameObject.SetActive(false);
        OnClosed?.Invoke(success);

     

        Destroy(gameObject);
    }


    // Add to AuthPanelController
    public void OpenLoginMode() { SwitchMode(Mode.Login); }
    public void OpenSignupMode() { SwitchMode(Mode.Signup); }

    void Update()
    {
        
    }

}