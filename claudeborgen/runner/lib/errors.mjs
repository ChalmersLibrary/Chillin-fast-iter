// A round that failed in an expected, explained way (red build, dirty tree, ...): reported to the
// app as-is, and the runner leaves the working tree alone beyond what it already did itself.
export class RoundFailure extends Error {}
