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
}
