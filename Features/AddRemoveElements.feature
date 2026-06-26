@ai_generated
Feature: Add Remove Elements
  Verifies dynamic element creation and deletion
  at https://the-internet.herokuapp.com/add_remove_elements/

  Scenario: Add an element and then delete it
    Given I navigate to the add remove elements page
    When I click the Add Element button
    Then a Delete button should be visible
    When I click the Delete button
    Then no Delete button should be visible

  Scenario: Add multiple elements
    Given I navigate to the add remove elements page
    When I click the Add Element button twice
    Then two Delete buttons should be present
