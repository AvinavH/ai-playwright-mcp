@ai_generated
Feature: Login Page
  Verifies the login form at https://the-internet.herokuapp.com/login

  Scenario: Successful login with valid credentials
    Given I am on the login page
    When I enter the username "tomsmith" and password "SuperSecretPassword!"
    And I click the Login button
    Then I should be redirected to the secure area
    And I should see a success flash message

  Scenario: Failed login with invalid credentials
    Given I am on the login page
    When I enter the username "wronguser" and password "badpassword"
    And I click the Login button
    Then I should see an error flash message
