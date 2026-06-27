namespace Playwright.AiFramework.Pages
{
    public class CheckboxesPage
    {
        private readonly IPage _page;

        public CheckboxesPage(IPage page)
        {
            _page = page;
        }

        public async Task NavigateToCheckboxesPage()
        {
            await _page.GotoAsync("/checkboxes");
        }

        public async Task CheckTheFirstCheckbox()
        {
            await _page.GetByRole(AriaRole.Checkbox).Nth(0).CheckAsync();
        }

        public async Task TheFirstCheckboxShouldBeChecked()
        {
            await Assertions.Expect(_page.GetByRole(AriaRole.Checkbox).Nth(0)).ToBeCheckedAsync();
        }

        public async Task UncheckTheSecondCheckbox()
        {
            await _page.GetByRole(AriaRole.Checkbox).Nth(1).UncheckAsync();
        }

        public async Task TheSecondCheckboxShouldNotBeChecked()
        {
            await Assertions.Expect(_page.GetByRole(AriaRole.Checkbox).Nth(1)).Not.ToBeCheckedAsync();
        }
    }
}