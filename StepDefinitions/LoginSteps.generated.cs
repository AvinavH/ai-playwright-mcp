[Binding]
public class LoginSteps
{
    private readonly LoginPage _loginPage;

    public LoginSteps(LoginPage loginPage)
    {
        _loginPage = loginPage;
    }

    [Given(@"I am on the login page")]
    public async Task GivenIAmOnTheLoginPage()
    {
        await _loginPage.IAmOnTheLoginPage();
    }

    [When(@"I enter the username ""(.*)"" and password ""(.*)""")]
    public async Task WhenIEnterTheUsernameAndPassword(string username, string password)
    {
        await _loginPage.IEnterTheUsernameAndPassword(username, password);
    }

    [When(@"I click the Login button")]
    public async Task WhenIClickTheLoginButton()
    {
        await _loginPage.IClickTheLoginButton();
    }

    [Then(@"I should see an error flash message")]
    public async Task ThenIShouldSeeAnErrorFlashMessage()
    {
        await _loginPage.IShouldSeeAnErrorFlashMessage();
    }

    [Then(@"I should be redirected to the secure area")]
    public async Task ThenIShouldBeRedirectedToTheSecureArea()
    {
        await _loginPage.IShouldBeRedirectedToTheSecureArea();
    }

    [Then(@"I should see a success flash message")]
    public async Task ThenIShouldSeeASuccessFlashMessage()
    {
        await _loginPage.IShouldSeeASuccessFlashMessage();
    }
}