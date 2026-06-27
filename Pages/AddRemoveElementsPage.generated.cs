namespace Playwright.AiFramework.Pages;

public class AddRemoveElementsPage
{
    private readonly IPage _page;

    public AddRemoveElementsPage(IPage page)
    {
        _page = page;
    }

    public async Task NavigateToAddRemoveElementsPage()
    {
        await _page.GotoAsync("/add_remove_elements/");
    }

    public async Task ClickAddElementButton()
    {
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add Element" }).ClickAsync();
    }

    public async Task DeleteButtonShouldBeVisible()
    {
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" })).ToBeVisibleAsync();
    }

    public async Task ClickDeleteButton()
    {
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" }).ClickAsync();
    }

    public async Task NoDeleteButtonShouldBeVisible()
    {
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" })).ToBeHiddenAsync();
    }

    public async Task ClickAddElementButtonTwice()
    {
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add Element" }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add Element" }).ClickAsync();
    }

    public async Task TwoDeleteButtonsShouldBePresent()
    {
        await Assertions.Expect(_page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" })).ToHaveCountAsync(2);
    }
}