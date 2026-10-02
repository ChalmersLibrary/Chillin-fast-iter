import { expect, Page } from "@playwright/test";
import { PASSWORD } from "../support/members";

/** The login page (/ChalmersILLLoginPage) - the one page an anonymous caller may reach. */
export class LoginPage {
  constructor(readonly page: Page) {}

  async goto() {
    await this.page.goto("/ChalmersILLLoginPage", { waitUntil: "domcontentloaded" });
    await expect(this.page.getByTestId("login-submit")).toBeVisible();
  }

  /** Submits the form and waits for the server's answer, whether it lets us in or not. */
  async submit(login: string, password: string = PASSWORD) {
    await this.page.getByTestId("login-username").fill(login);
    await this.page.getByTestId("login-password").fill(password);
    await Promise.all([
      this.page.waitForNavigation({ waitUntil: "load" }),
      this.page.getByTestId("login-submit").click(),
    ]);
  }

  get error() {
    return this.page.getByTestId("login-error");
  }

  /**
   * Checks that we are actually logged in, by reaching a page that requires it.
   *
   * Deliberately not an assertion about where login lands: HandleLogin sends every account with
   * the Desk role to /disk/ (the page that says the disk app is no longer used), and only
   * everyone else to the order list. Which of those is right is an open question - see
   * SCENARIOS.md - and no scenario here should quietly depend on today's answer.
   */
  async expectLoggedIn() {
    await this.page.goto("/bestaellningar/", { waitUntil: "load" });
    await expect(this.page).toHaveURL(/bestaellningar/);
    await expect(this.page.getByTestId("order-row").first()).toBeVisible();
  }
}
