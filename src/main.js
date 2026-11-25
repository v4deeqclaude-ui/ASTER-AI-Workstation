const {app,BrowserWindow,ipcMain,safeStorage,shell,dialog}=require("electron");
const path=require("node:path"),fs=require("node:fs/promises");
const file=()=>path.join(app.getPath("userData"),"aster-data.json");
async function read(){try{return JSON.parse(await fs.readFile(file(),"utf8"))}catch{return {conversations:[],apps:[],settings:{}}}}
async function write(d){await fs.mkdir(path.dirname(file()),{recursive:true});await fs.writeFile(file(),JSON.stringify(d,null,2),"utf8")}
function create(){const w=new BrowserWindow({width:1400,height:900,minWidth:950,minHeight:620,backgroundColor:"#101114",webPreferences:{preload:path.join(__dirname,"preload.js"),contextIsolation:true,nodeIntegration:false,sandbox:true}});w.loadFile(path.join(__dirname,"index.html"))}
app.whenReady().then(()=>{
 ipcMain.handle("data:read",read);ipcMain.handle("data:write",async(_,d)=>{await write(d);return true});
 ipcMain.handle("key:save",async(_,v)=>{const d=await read(),enc=safeStorage.isEncryptionAvailable();d.settings.apiKey=enc?safeStorage.encryptString(String(v)).toString("base64"):String(v);d.settings.keyEncrypted=enc;await write(d);return {encrypted:enc}});
 ipcMain.handle("key:read",async()=>{const d=await read(),v=d.settings.apiKey;if(!v)return "";try{return d.settings.keyEncrypted&&safeStorage.isEncryptionAvailable()?safeStorage.decryptString(Buffer.from(v,"base64")):v}catch{return ""}});
 ipcMain.handle("app:add",async()=>{const r=await dialog.showOpenDialog({properties:["openFile"],filters:[{name:"Программы",extensions:["exe"]}]});return r.canceled?null:r.filePaths[0]});
 ipcMain.handle("app:launch",async(_,p)=>{if(typeof p!=="string"||!p.toLowerCase().endsWith(".exe"))throw Error("Разрешены только EXE");return shell.openPath(p)});
 create();app.on("activate",()=>{if(!BrowserWindow.getAllWindows().length)create()});
});app.on("window-all-closed",()=>{if(process.platform!=="darwin")app.quit()});