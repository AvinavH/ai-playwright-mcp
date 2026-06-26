@ai_generated
Feature: Checkboxes Page
  Verifies checkbox interactions at https://the-internet.herokuapp.com/checkboxes
  Demonstrates the use of role:checkbox + index for elements with no accessible name

  Scenario: Check the first checkbox
    Given I navigate to the checkboxes page
    When I check the first checkbox
    Then the first checkbox should be checked

  Scenario: Uncheck the second checkbox
    Given I navigate to the checkboxes page
    When I uncheck the second checkbox
    Then the second checkbox should not be checked
