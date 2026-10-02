import { pbkdf2Sync, randomBytes } from "crypto";
import * as fs from "fs";
import * as path from "path";

// Accounts the e2e tests can log in as. The app deliberately doesn't seed members.json itself
// (creating the first account is a one-time manual step in real deployments), so the harness
// does it - the same thing IsolatedModeSmokeTest does on the C# side.
// The roles the real accounts have: everyone who works in the system and may change anything
// carries Administrator. "Desk" is out of use entirely (Lars 2026-10-02) and is deliberately not
// handed out here - among other things it is what sends a login to /disk/, the page that says the
// disk app is no longer used.
export type Role = "superadmin" | "admin" | "viewer" | "roleless";

export const PASSWORD = "e2e-password"; // throwaway DataPath only, never a real credential

const ROLES: Record<Role, string[]> = {
  superadmin: ["Administrator", "SuperAdmin"],
  admin: ["Administrator"],
  viewer: ["Viewer"],
  roleless: [],
};

// ASP.NET Core Identity PasswordHasherCompatibilityMode.IdentityV2 (what FileMembershipProvider
// uses): base64( 0x00 | salt[16] | PBKDF2-HMAC-SHA1(password, salt, 1000 iterations)[32] ).
function hashPasswordIdentityV2(password: string): string {
  const salt = randomBytes(16);
  const subkey = pbkdf2Sync(password, salt, 1000, 32, "sha1");
  return Buffer.concat([Buffer.from([0x00]), salt, subkey]).toString("base64");
}

export function writeMembersFile(dataPath: string): void {
  fs.mkdirSync(dataPath, { recursive: true });
  const hash = hashPasswordIdentityV2(PASSWORD);
  const accounts = (Object.keys(ROLES) as Role[]).map((login) => ({
    Login: login,
    PasswordHash: hash,
    Roles: ROLES[login],
  }));
  fs.writeFileSync(path.join(dataPath, "members.json"), JSON.stringify(accounts, null, 2));
}
