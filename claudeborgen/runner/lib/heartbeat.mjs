// Claude's status as the app shows it. Sent on a timer, independent of what the round is doing -
// a long build or a long Claude turn must not look like "offline", and a hung or dead runner must
// (DESIGN.md: the status is the runner's, not the model's). Also sent immediately on every change.
export class Heartbeat {
  constructor({ api, intervalSeconds, log = () => {} }) {
    this.api = api;
    this.intervalMs = intervalSeconds * 1000;
    this.log = log;
    this.state = { activity: "idle", conversationId: null, phase: null };
    this.timer = null;
    this.paused = false;
  }

  start() {
    this.timer = setInterval(() => this.send(), this.intervalMs);
    this.timer.unref?.();
    return this.send();
  }

  stop() {
    clearInterval(this.timer);
    this.timer = null;
  }

  working(conversationId, phase) {
    this.state = { activity: "working", conversationId, phase };
    return this.send();
  }

  idle() {
    this.state = { activity: "idle", conversationId: null, phase: null };
    return this.send();
  }

  // Only for the fake runner's [offline] marker: stop sending for a while.
  pause() { this.paused = true; }
  resume() { this.paused = false; return this.send(); }

  async send() {
    if (this.paused) return;
    const { activity, conversationId, phase } = this.state;
    try {
      await this.api.heartbeat(activity, conversationId, phase);
    } catch (e) {
      this.log(`Hjärtslaget misslyckades: ${e.message}`);
    }
  }
}
