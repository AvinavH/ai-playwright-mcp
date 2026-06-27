[Binding]
public class CheckboxesSteps
{
    private readonly CheckboxesPage _checkboxesPage;

    public CheckboxesSteps(CheckboxesPage checkboxesPage)
    {
        _checkboxesPage = checkboxesPage;
    }

    [Given("I navigate to the checkboxes page")]
    public async Task GivenINavigateToTheCheckboxesPage()
    {
        await _checkboxesPage.NavigateToCheckboxesPage();
    }

    [When("I check the first checkbox")]
    public async Task WhenICheckTheFirstCheckbox()
    {
        await _checkboxesPage.CheckTheFirstCheckbox();
    }

    [Then("the first checkbox should be checked")]
    public async Task ThenTheFirstCheckboxShouldBeChecked()
    {
        await _checkboxesPage.TheFirstCheckboxShouldBeChecked();
    }

    [When("I uncheck the second checkbox")]
    public async Task WhenIUncheckTheSecondCheckbox()
    {
        await _checkboxesPage.UncheckTheSecondCheckbox();
    }

    [Then("the second checkbox should not be checked")]
    public async Task ThenTheSecondCheckboxShouldNotBeChecked()
    {
        await _checkboxesPage.TheSecondCheckboxShouldNotBeChecked();
    }
}