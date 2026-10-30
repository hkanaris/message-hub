// Injected into every pane before the page's own scripts run.
// Replaces the browser's Notification API so each app's notifications come to
// Message Hub instead of Windows, and reports when the page has focus. Each
// notification has an id, and the app closing it (usually because the chat was
// read) is reported too, so the hub's feed can drop it.
(() => {
  if (window.__hubInstalled || window.top !== window) return;
  window.__hubInstalled = true;

  const send = (msg) => {
    try { window.chrome.webview.postMessage(JSON.stringify(msg)); } catch (e) {}
  };

  let last = null;
  let nextId = 1;

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
      this.__id = nextId++;
      last = this;
      send({
        type: 'notification', id: this.__id, tag: String(this.tag), title: this.title, body: this.body,
        test: this.tag === '__hub_test',
      });
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

    close() {
      if (this.__closed) return;
      this.__closed = true;
      if (last === this) last = null;
      send({ type: 'closed', id: this.__id });
      this.__fire('close');
    }

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

  // Notifications shown through a service worker registration. Apps close these by asking
  // getNotifications() for them, so keep the open ones.
  if (window.ServiceWorkerRegistration) {
    const shown = [];
    ServiceWorkerRegistration.prototype.showNotification = function (title, options) {
      options = options || {};
      last = null; // nothing to click for these, so don't reopen an older one
      const tag = String(options.tag ?? '');
      for (let i = shown.length - 1; i >= 0; i--) {
        if (tag && shown[i].tag === tag) shown.splice(i, 1);
      }
      const note = {
        id: nextId++, tag, title: String(title ?? ''), body: String(options.body ?? ''), data: options.data ?? null,
        close() {
          const i = shown.indexOf(note);
          if (i >= 0) shown.splice(i, 1);
          send({ type: 'closed', id: note.id });
        },
      };
      shown.push(note);
      send({ type: 'notification', id: note.id, tag, title: note.title, body: note.body });
      return Promise.resolve();
    };
    ServiceWorkerRegistration.prototype.getNotifications = function (filter) {
      const tag = filter && filter.tag;
      return Promise.resolve(shown.filter((n) => !tag || n.tag === tag));
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
