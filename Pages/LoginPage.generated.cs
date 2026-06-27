namespace Playwright.AiFramework.Pages
{
    public class LoginPage
    {
        private readonly IPage _page;

        public LoginPage(IPage page) { _page = page; }

        public async Task IAmOnTheLoginPage()
        {
            await _page.GotoAsync("https://the-internet.herokuapp.com/login");
        }

        public async Task IEnterTheUsernameAndPassword(string username, string password)
        {
            await _page.GetByLabel("Username").FillAsync(username);
            await _page.GetByLabel("Password").FillAsync(password);
        }

        public async Task IClickTheLoginButton()
        {
            await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Login" }).ClickAsync();
        }

        public async Task IShouldSeeAnErrorFlashMessage()
        {
            await Assertions.Expect(_page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        }

        public async Task IShouldBeRedirectedToTheSecureArea()
        {
            await Assertions.Expect(_page).ToHaveURLAsync("https://the-internet.herokuapp.com/secure");
        }

        public async Task IShouldSeeASuccessFlashMessage()
        {
            await Assertions.Expect(_page.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        }
    }
}