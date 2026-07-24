# 2026-07-24 CP/AP 三端从零生产发布

## Host 2.0.2

活动工序收敛为 CP/AP，移除 Homogenization 的 Launcher、profile、发布和兼容输入。通用 PLC、DataPipeline、Cloud/MES 队列及补传链保持不变。

## AP/CP 2.0.1

双弹夹换号与连续三次空值完工、精确一次入队、MES/Cloud 身份隔离、稳定 PLC 编码、中文名称、UTC 持久化和 MES 业务时区转换。

## Homogenization 退役

Homogenization 只保留历史审计并归档，不进入活动 catalog 或安装素材。

## 安全边界

本次部署不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
