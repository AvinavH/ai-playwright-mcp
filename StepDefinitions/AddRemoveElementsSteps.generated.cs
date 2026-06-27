[Binding]
public class AddRemoveElementsSteps
{
    private readonly AddRemoveElementsPage _addRemoveElementsPage;

    public AddRemoveElementsSteps(AddRemoveElementsPage addRemoveElementsPage)
    {
        _addRemoveElementsPage = addRemoveElementsPage;
    }

    [Given("I navigate to the add remove elements page")]
    public async Task GivenINavigateToTheAddRemoveElementsPage()
    {
        await _addRemoveElementsPage.NavigateToAddRemoveElementsPage();
    }

    [When("I click the Add Element button")]
    public async Task WhenIClickTheAddElementButton()
    {
        await _addRemoveElementsPage.ClickAddElementButton();
    }

    [Then("a Delete button should be visible")]
    public async Task ThenADeleteButtonShouldBeVisible()
    {
        await _addRemoveElementsPage.DeleteButtonShouldBeVisible();
    }

    [When("I click the Delete button")]
    public async Task WhenIClickTheDeleteButton()
    {
        await _addRemoveElementsPage.ClickDeleteButton();
    }

    [Then("no Delete button should be visible")]
    public async Task ThenNoDeleteButtonShouldBeVisible()
    {
        await _addRemoveElementsPage.NoDeleteButtonShouldBeVisible();
    }

    [When("I click the Add Element button twice")]
    public async Task WhenIClickTheAddElementButtonTwice()
    {
        await _addRemoveElementsPage.ClickAddElementButtonTwice();
    }

    [Then("two Delete buttons should be present")]
    public async Task ThenTwoDeleteButtonsShouldBePresent()
    {
        await _addRemoveElementsPage.TwoDeleteButtonsShouldBePresent();
    }
}