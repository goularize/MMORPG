using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Shared.Constants;
using Shared.Network;

namespace Client.UI
{
    /// <summary>
    /// Controls the Authentication scene: a Sign In panel and a Sign Up panel, only one visible at a time.
    /// This component must live on an object that stays active (the panels are toggled with SetActive), because
    /// <see cref="Client.Network.Handlers.AuthHandler"/> looks it up to report the server's answer.
    /// </summary>
    public class AuthenticationUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject signInPanel;
        [SerializeField] private GameObject signUpPanel;

        [Header("Sign In")]
        [SerializeField] private TMP_InputField signInUsernameInput;
        [SerializeField] private TMP_InputField signInPasswordInput;
        [SerializeField] private Button signInButton;
        [SerializeField] private Button showSignUpButton;
        [SerializeField] private TextMeshProUGUI signInErrorText;

        [Header("Sign Up")]
        [SerializeField] private TMP_InputField signUpUsernameInput;
        [SerializeField] private TMP_InputField signUpEmailInput;
        [SerializeField] private TMP_InputField signUpPasswordInput;
        [SerializeField] private TMP_InputField signUpConfirmPasswordInput;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button showSignInButton;
        [SerializeField] private TextMeshProUGUI signUpErrorText;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer before unlocking the form again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

        private bool _pending;
        private float _pendingSince;

        private bool SignUpVisible => signUpPanel != null && signUpPanel.activeSelf;

        private void Start()
        {
            ConfigureInput(signInUsernameInput, InputRules.UsernameMaxLength, TMP_InputField.ContentType.Standard);
            ConfigureInput(signInPasswordInput, InputRules.PasswordMaxBytes, TMP_InputField.ContentType.Password);
            ConfigureInput(signUpUsernameInput, InputRules.UsernameMaxLength, TMP_InputField.ContentType.Standard);
            ConfigureInput(signUpEmailInput, InputRules.EmailMaxLength, TMP_InputField.ContentType.EmailAddress);
            ConfigureInput(signUpPasswordInput, InputRules.PasswordMaxBytes, TMP_InputField.ContentType.Password);
            ConfigureInput(signUpConfirmPasswordInput, InputRules.PasswordMaxBytes, TMP_InputField.ContentType.Password);

            signInButton.onClick.AddListener(OnSignInClicked);
            signUpButton.onClick.AddListener(OnSignUpClicked);
            showSignUpButton.onClick.AddListener(ShowSignUp);
            showSignInButton.onClick.AddListener(ShowSignIn);

            // Enter submits from any field of the visible form
            signInUsernameInput.onSubmit.AddListener(_ => OnSignInClicked());
            signInPasswordInput.onSubmit.AddListener(_ => OnSignInClicked());
            signUpUsernameInput.onSubmit.AddListener(_ => OnSignUpClicked());
            signUpEmailInput.onSubmit.AddListener(_ => OnSignUpClicked());
            signUpPasswordInput.onSubmit.AddListener(_ => OnSignUpClicked());
            signUpConfirmPasswordInput.onSubmit.AddListener(_ => OnSignUpClicked());

            ShowSignIn();
        }

        private void Update()
        {
            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                ReEnableButtons("The server did not respond. Please try again.");
            }
        }

        private static void ConfigureInput(TMP_InputField field, int characterLimit, TMP_InputField.ContentType contentType)
        {
            field.characterLimit = characterLimit;
            field.contentType = contentType;
            field.ForceLabelUpdate();
        }

        public void ShowSignIn()
        {
            if (_pending) return;
            signInPanel.SetActive(true);
            signUpPanel.SetActive(false);
            SetError("");
            signInUsernameInput.Select();
        }

        public void ShowSignUp()
        {
            if (_pending) return;
            signUpPanel.SetActive(true);
            signInPanel.SetActive(false);
            SetError("");
            signUpUsernameInput.Select();
        }

        private void OnSignInClicked()
        {
            if (_pending || SignUpVisible) return;

            string username = signInUsernameInput.text.Trim();
            string password = signInPasswordInput.text;

            // The same rules the server enforces, so an obviously bad form never costs a lockout attempt
            string error = InputRules.ValidateUsername(username) ?? InputRules.ValidatePassword(password);
            if (error != null)
            {
                SetError(error);
                return;
            }

            using (Packet packet = new Packet(OpCode.SignInRequest))
            {
                packet.Write(Network.NetworkManager.GameVersion);
                packet.Write(username);
                packet.Write(password);
                Network.NetworkManager.Instance.SendPacket(packet);
            }

            BeginPending();
        }

        private void OnSignUpClicked()
        {
            if (_pending || !SignUpVisible) return;

            string username = signUpUsernameInput.text.Trim();
            string email = InputRules.NormalizeEmail(signUpEmailInput.text);
            string password = signUpPasswordInput.text;
            string confirmation = signUpConfirmPasswordInput.text;

            string error = InputRules.ValidateUsername(username)
                ?? InputRules.ValidateEmail(email)
                ?? InputRules.ValidatePassword(password);
            if (error == null && !string.Equals(password, confirmation, System.StringComparison.Ordinal))
            {
                error = "Passwords do not match.";
            }
            if (error != null)
            {
                SetError(error);
                return;
            }

            using (Packet packet = new Packet(OpCode.SignUpRequest))
            {
                packet.Write(Network.NetworkManager.GameVersion);
                packet.Write(username);
                packet.Write(password);
                packet.Write(email);
                Network.NetworkManager.Instance.SendPacket(packet);
            }

            BeginPending();
        }

        private void BeginPending()
        {
            _pending = true;
            _pendingSince = Time.unscaledTime;
            SetError("");
            SetInteractable(false);
        }

        private void SetInteractable(bool interactable)
        {
            signInButton.interactable = interactable;
            showSignUpButton.interactable = interactable;
            signUpButton.interactable = interactable;
            showSignInButton.interactable = interactable;
        }

        private void SetError(string message)
        {
            if (signInErrorText != null) signInErrorText.text = SignUpVisible ? "" : message;
            if (signUpErrorText != null) signUpErrorText.text = SignUpVisible ? message : "";
        }

        /// <summary>Called by AuthHandler when the server rejects a request, and by the response timeout.</summary>
        public void ReEnableButtons(string errorMessage = "")
        {
            _pending = false;
            SetInteractable(true);

            // Never leave the password in the form after a failed attempt
            signInPasswordInput.text = "";
            signUpPasswordInput.text = "";
            signUpConfirmPasswordInput.text = "";

            if (!string.IsNullOrEmpty(errorMessage)) SetError(errorMessage);
        }
    }
}
