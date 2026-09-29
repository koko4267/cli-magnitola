const { app, BrowserWindow, shell } = require('electron');
const path = require('path');

// Set application identity so Windows Taskbar uses our logo instead of Electron's
if (process.platform === 'win32') {
  app.setAppUserModelId('com.magnitola.downloader');
}

function createWindow() {
  const iconPath = process.platform === 'win32'
    ? path.join(__dirname, 'icon.ico')
    : path.join(__dirname, 'magnitolalogo.png');

  const win = new BrowserWindow({
    width: 620,
    height: 380,
    minWidth: 500,
    minHeight: 330,
    title: 'MAGNITOLA',
    icon: iconPath,
    autoHideMenuBar: true,
    backgroundColor: '#4c5844',
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
      webSecurity: false,
      allowRunningInsecureContent: true
    }
  });

  // Open external links (such as https://sync-line.pages.dev/) in the default web browser
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('http:') || url.startsWith('https:')) {
      shell.openExternal(url);
      return { action: 'deny' };
    }
    return { action: 'allow' };
  });

  win.loadFile('index.html');
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createWindow();
    }
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});
