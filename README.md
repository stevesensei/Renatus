# Renatus
> Yet another open-source Minecraft 3rd-party Skin Server.Based on [Yggdrasil API](https://yushijinhun.github.io/authlib-injector/zh/Home.html)

## 前情提要

暂时懒得写太多东西，等有时间再说。

这个项目是因为各种原因需要一堆客制化的皮肤站功能，但是blessingskin是php写的我~~搞不会~~，所以这个项目就这么稀里糊涂的诞生了。

肝了两天，用AI糊了一个差不多的（在此感谢codex的两次重置和opencode剩下的额度），有一些东西模仿的blessingskin，在此先提出感谢（之后正式版本的markdown会写更多内容）

bug方面会慢慢修，我不是那种纯vibe coding选手，写出来的代码我还是会审核的。不用担心我跑路。

## 架构

项目采用的是Blazor框架，对没错，使用的是SSR的技术路线没有做前后端分离。

~~总感觉历史在倒退，不知道是不是我的幻觉~~

不过也提供了一些API接口，可以访问``/scalar``来看接口文档（仅限debug模式）

Docker部署？如果我用aspire的话或许好说，现在可能费点事

## 注册邮箱域名限制

在 `appsettings.json` 的 `Auth` 节中配置：

```json
"RegistrationEmailDomains": {
  "Enabled": true,
  "Mode": "Whitelist",
  "Domains": ["s.hlju.edu.cn", "stu.hlju.edu.cn", "hlju.edu.cn"]
}
```

`Enabled` 为 `false` 时关闭限制；`Mode` 支持 `Whitelist`（仅允许列表内域名）和 `Blacklist`（禁止列表内域名）。域名精确匹配、忽略大小写，可带或不带 `@` 前缀，不会自动包含子域名。白名单为空时禁止所有注册，黑名单为空时不限制域名。未配置此节时默认关闭限制；配置模板默认启用上述三个域名的白名单。

该限制仅作用于注册，不影响已有用户登录或修改邮箱。修改配置后重启应用生效。

## 截图

完全不会前端，拿AI糊的，不过还是可以的

<details>
<summary>点击查看</summary>

主页

![](assets/mainpage.png)

登录

![](assets/login.png)

仪表盘

![](assets/dashboard.png)

材质库

![](assets/library.png)

预览

![](assets/preview.png)

</details>

## 其他

暂时没想到
