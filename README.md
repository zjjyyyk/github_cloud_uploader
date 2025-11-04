# ZJJYYYK Cloud Uploader

一个简单的 Windows 文件云同步工具。

## 用途

将本地文件快速上传到 Git 仓库，支持右键菜单和拖拽两种方式。

## 界面

![主界面](screenshot.png)

## 运行

```bash
# 发布为单文件可执行程序（需要 .NET 9.0 运行时）
dotnet publish CloudUploader\CloudUploader.csproj -c Release -r win-x64 -p:PublishSingleFile=true

# 运行发布后的程序
.\CloudUploader\bin\Release\net9.0-windows\win-x64\publish\CloudUploader.exe
```

