using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Text.RegularExpressions;

class Ad {
  public async Task Login(string user, string password) {
    await Post("/login", "Password", password, "Email address", user);
  }

  public async Task<string> SubmitDscan(string text) {
    var resp = await Post("/intel", "Paste anything", text, "submit", "new");
    string uri = resp.ResponseUri.ToString();
    return uri;
  }

  async Task<HttpWebResponse> Post(string endpoint, params string[] args) {
    var formBuilder = new StringBuilder();
    if ((args.Length & 1) != 0)
      throw new ArgumentException(
          "must pass an even amount of key/value args", "args");
    for (var i = 0; i < args.Length; i += 2) {
      formBuilder.Append(WebUtility.UrlEncode(args[i]));
      formBuilder.Append('=');
      formBuilder.Append(WebUtility.UrlEncode(args[i + 1]));
      if (i + 2 < args.Length)
        formBuilder.Append('&');
    }
    var data = Encoding.UTF8.GetBytes(formBuilder.ToString());
    var url = urlBase + endpoint;
    var req = (HttpWebRequest) WebRequest.Create(url);
    req.CookieContainer = cookies;
    req.Method = "POST";
    req.ContentType = "application/x-www-form-urlencoded";
    req.ContentLength = data.Length;
    GLib.Timeout.Add(10000, delegate {
      req.Abort();
      return false;
    });
    HttpWebResponse resp = null;
    try {
      using (Stream stream = await req.GetRequestStreamAsync())
        await stream.WriteAsync(data, 0, data.Length);
      resp = (HttpWebResponse) await req.GetResponseAsync();
    } catch (WebException e) {
      if (e.Status == WebExceptionStatus.RequestCanceled)
        throw new WebException("Request timed out", null,
                               WebExceptionStatus.Timeout, resp);
      throw;
    }
    if (resp.StatusCode != HttpStatusCode.OK) {
      throw new WebException("HTTP endpoint returned an error", null,
          (WebExceptionStatus) resp.StatusCode, resp);
    }
    return resp;
  }

  static readonly string urlBase = "https://adashboard.info";

  public readonly CookieContainer cookies = new CookieContainer();
}

class LoginWindow : Gtk.Window {
  public LoginWindow() : base("Log into aDashboard") {
    this.Resizable = false;
    this.BorderWidth = 10;
    var vbox = new Gtk.VBox(false, 16);

    var table = new Gtk.Table(2, 2, false);
    vbox.Add(table);

    var label = new Gtk.Label("Email address: ");
    label.SetAlignment(0, 1);
    table.Attach(label, 0, 1, 0, 1);

    label = new Gtk.Label("Password: ");
    label.SetAlignment(0, 1);
    table.Attach(label, 0, 1, 1, 2);

    var userEntry = new Gtk.Entry();
    table.Attach(userEntry, 1, 2, 0, 1);

    var passwordEntry = new Gtk.Entry();
    passwordEntry.Visibility = false;
    table.Attach(passwordEntry, 1, 2, 1, 2);

    var align = new Gtk.Alignment(1f, 0f, 0f, 0f);
    vbox.Add(align);

    var okButton = new Gtk.Button(Gtk.Stock.Ok);
    align.Add(okButton);
    EventHandler go = delegate {
      if (LoginDataEntered != null)
        LoginDataEntered(userEntry.Text, passwordEntry.Text);
    };
    okButton.Clicked += go;
    userEntry.Activated += go;
    passwordEntry.Activated += go;
    this.DeleteEvent += delegate { Gtk.Application.Quit(); };
    this.Add(vbox);
    vbox.ShowAll();
  }

  public delegate void LoginCallback(string user, string password);
  public event LoginCallback LoginDataEntered;
}

public class AdSubmit : Gtk.Window {
  static void Main(string[] args) {
    Gtk.Application.Init();

    var login = new LoginWindow();

    login.LoginDataEntered += async (user, password) => {
      login.Sensitive = false;

      var aD = new Ad();

      try {
        await aD.Login(user, password);

        int x, y, loginW, loginH, w, h;
        login.GetPosition(out x, out y);
        login.GetSize(out loginW, out loginH);
        login.Destroy();
        var wnd = new AdSubmit(aD);
        wnd.GetSize(out w, out h);
        wnd.Move(x - w / 2 + loginW / 2, y - h / 2 + loginH / 2);
        wnd.Show();
      } catch (WebException e) {
        DisplayWebException(login, () => { login.Sensitive = true; },
                            "Error logging in", e);
      }
    };

    login.Show();

    Gtk.Application.Run();
  }

  AdSubmit(Ad aD) : base("aD dscan submit") {
    this.DeleteEvent += delegate { Gtk.Application.Quit(); };
    this.aD = aD;
    var vbox = new Gtk.VBox(false, 8);
    this.BorderWidth = 12;
    this.SetDefaultSize(512, -1);

    button = new Gtk.Button("Submit clipboard");
    button.CanDefault = true;

    button.Clicked += delegate {
      clipboard.RequestText(async (c, text) => {
        var result = await Submit(text);
        if (result != null)
          clipboard.Text = result;
      });
    };

    vbox.PackStart(button, false, false, 0);

    var tree = new Gtk.TreeView();
    var colTime = new Gtk.TreeViewColumn();
    var colLink = new Gtk.TreeViewColumn();

    colTime.Title = "Time";
    colLink.Title = "Link";

    tree.AppendColumn(colTime);
    tree.AppendColumn(colLink);

    var cellTime = new Gtk.CellRendererText();
    var cellLink = new Gtk.CellRendererText();

    colTime.PackStart(cellTime, true);
    colLink.PackStart(cellLink, true);

    colTime.AddAttribute(cellTime, "text", 0);
    colLink.AddAttribute(cellLink, "text", 1);

    colLink.Expand = true;

    store = new Gtk.ListStore(typeof(string), typeof(string), typeof(string));
    tree.Model = store;

    scroll = new Gtk.ScrolledWindow();
    scroll.ShadowType = Gtk.ShadowType.In;
    scroll.Add(tree);
    scroll.SetSizeRequest(-1, 192);
    tree.SetSizeRequest(-1, 192);
    vbox.Add(scroll);

    this.Add(vbox);
    vbox.ShowAll();
  }

  async Task<string> Submit(string text) {
    this.Sensitive = false;
    Action restore = () => { this.Sensitive = true; };
    if (string.IsNullOrWhiteSpace(text)) {
      ShowError(this, restore, "No dscan in clipboard",
                "Clipboard is empty, please copy a dscan.");
      return null;
    }
    try {
      var url = await aD.SubmitDscan(text);

      store.AppendValues(DateTime.UtcNow.ToString("%HH:%MM:%ss"), url);
      GLib.Idle.Add(() => {
        var adj = scroll.Vadjustment;
        adj.Value = adj.Upper;
        adj.ChangeValue();

        return false;
      });

      restore();
      return url;
    } catch (WebException e) {
      DisplayWebException(this, restore, "Network error submitting dscan", e);
      return null;
    }
  }

  static void DisplayWebException(Gtk.Window parent, Action after,
                                  string title, WebException e) {
    var msg = e.Message;
    var i = e.InnerException;
    msg += "\nStatus: " + e.Status;
    if (i != null)
      msg += "\n" + i.Message;
    ShowError(parent, after, title, msg);
  }

  static void ShowError(Gtk.Window parent, Action after,
                        string title, string body) {
    var err =
      new Gtk.MessageDialog(parent,
          Gtk.DialogFlags.DestroyWithParent,
          Gtk.MessageType.Error,
          Gtk.ButtonsType.Ok,
          "{0}", body);
    err.Title = title;
    Action del = () => {
      after();
      err.Destroy();
    };
    err.Response += delegate { del(); };
    err.DeleteEvent += delegate { del(); };
    err.Show();
  }

  readonly Ad aD = new Ad();
  readonly Gtk.Button button;
  readonly Gtk.ListStore store;
  readonly Gtk.ScrolledWindow scroll;
  readonly Gtk.Clipboard clipboard = Gtk.Clipboard.Get(Gdk.Selection.Clipboard);
}
