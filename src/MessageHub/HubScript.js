// Injected into every pane before the page's own scripts run.
// Replaces the browser's Notification API so each app's notifications come to
// Message Hub instead of Windows, and reports when the page has focus.
(() => {
  if (window.__hubInstalled || window.top !== window) return;
  window.__hubInstalled = true;

  const send = (msg) => {
    try { window.chrome.webview.postMessage(JSON.stringify(msg)); } catch (e) {}
  };

  let last = null;

  class HubNotification extends EventTarget {
    constructor(title, options) {
      super();
      options = options || {};
      this.title = String(title ?? '');
      this.body = String(options.body ?? '');
      this.tag = options.tag ?? '';
      this.data = options.data ?? null;
      this.icon = options.icon ?? '';
      this.onclick = this.onclose = this.onshow = this.onerror = null;
      last = this;
      send({ type: 'notification', title: this.title, body: this.body, test: this.tag === '__hub_test' });
      setTimeout(() => this.__fire('show'), 0);
    }

    __fire(name) {
      const event = new Event(name, { cancelable: true });
      const handler = this['on' + name];
      if (typeof handler === 'function') {
        try { handler.call(this, event); } catch (e) {}
      }
      this.dispatchEvent(event);
    }

    close() { this.__fire('close'); }

    static get permission() { return 'granted'; }

    static requestPermission(callback) {
      if (typeof callback === 'function') callback('granted');
      return Promise.resolve('granted');
    }
  }
  Object.defineProperty(HubNotification, 'maxActions', { value: 0 });
  window.Notification = HubNotification;

  // Clicking a pane's header "clicks" its latest notification, so the app opens that chat.
  window.__hubOpenLast = () => {
    if (!last) return;
    window.focus();
    last.__fire('click');
    last = null;
  };

  if (window.ServiceWorkerRegistration) {
    ServiceWorkerRegistration.prototype.showNotification = function (title, options) {
      send({ type: 'notification', title: String(title ?? ''), body: String((options && options.body) ?? '') });
      return Promise.resolve();
    };
    ServiceWorkerRegistration.prototype.getNotifications = function () {
      return Promise.resolve([]);
    };
  }

  if (navigator.permissions && navigator.permissions.query) {
    const query = navigator.permissions.query.bind(navigator.permissions);
    navigator.permissions.query = (descriptor) => {
      if (descriptor && descriptor.name === 'notifications') {
        const status = new EventTarget();
        status.name = 'notifications';
        status.state = 'granted';
        status.onchange = null;
        return Promise.resolve(status);
      }
      return query(descriptor);
    };
  }

  window.addEventListener('focus', () => send({ type: 'focus', value: true }));
  window.addEventListener('blur', () => send({ type: 'focus', value: false }));
  window.addEventListener('pointerdown', () => send({ type: 'focus', value: true }), true);
})();
