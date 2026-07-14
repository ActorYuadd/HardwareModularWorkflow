# - HardwareModularWorkflow

HardwareModularWorkflow 是一款硬件模块化工作流软件。根据编辑的工作流进行独立运动，亦可 进行同步（等待某工作流完成）
在设计理念中可以在工作流中可以添加其他工作流（对于当前工作流的引用不可取），


UI画风参考：https://github.com/yoi102/VisionProcess 

## 项目

当前 `HardwareModularWorkflow.sln` 中的项目如下：
```text
HardwareModularWorkflow.Core
HardwareModularWorkflow.Db
HardwareModularWorkflow.Wpf
HardwareModularWorkflow.Hardware
HardwareModularWorkflow.Workflow
HardwareModularWorkflow.MotioncontrolPlc
```
View / ViewModel 放在 Wpf 中

# - 框架分层
```mermaid
flowchart TD
    Ui["Wpf Ui<br/>***.Wpf.Views<br/>***.Wpf.ViewModels"]
    Db["Hardware Data Model<br/>***.Db.Services<br/>***"]
    Plc["Control Plc<br/>***.MotioncontrolPlc.Dlls<br/>***.MotioncontrolPlc.Services"]
    Hardware["Hardware Operate<br/>***.Hardware.Models"]
    

    UI->Core
    Core->Db

```

## 项目职责

### .Core

调度各库的操作
  
### .Db

数据模型层，是 模块、流参数

主要职责：

- 定义 `HardwareDocument`、Communication、


### .Wpf

### .Hardware

### .Workflow

### .MotioncontrolPlc

### .命令


## 类型_定义

硬件： 关键元素，应该是可自定义并且多样化类型
命令： 发/收命令，线程池处理加安全线程（是否返回执行结果，返回结果的打印日志） 

模块： 自定义（几个硬件组合最小单元，工作流最小的模块（添加硬件/自定义）） 
流： 由单个或多个 模块 组合的工作流 可单独起别名 某某_模组
引用： 将引用的上级提供导航    
层次： 目前工作流所在层次

```
    流->模块->硬件->命令
```


## 内容 属性
    
模块： 由单个或多个 #硬件 组成的工作流 可单独起别名 某某_模块 内容：“名称、标签、备注、核心事件、通知事件、”
模组： 由单个或多个 #模块 组合的工作流 可单独起别名 某某_模组
工作流属性： 名称、标签、备注、核心事件、通知事件、

硬件： 节点索引信息（Id、名称、端口、地址） ，类型信息（电机、制温、制冷、自定义）决定硬件命令方式
    电机： 基本属性（编码，移动，速度，扭力等）
硬件类型的自定义详情： 提供json定义数据类型 将数据解析到 数据库中保存使用 但建表？？？还是 保存json文件
{
    Name:string,
    Id:long,
    Allas:string,
    Note:string,
    Custon:// 自定义项
    [
        X:int,
        Y:int,
        Z:int,
        Pa:double
    ]
}
配置： 通道（指的 Can/Plc等通道）、及其他自定义配置、数据上传服务地址、

## 流程
     
工作流
    执行流程示例：模块（启动顺序第一个硬件） -> 模组（第一个模块）
    异步动作流程等待 模组->模块内 步骤完成

示例：试想 一个模块只能处理一个动作 为 R ，一个模组是连续动作 D ，

对于工作流 循环嵌套 使用的流或模块 进行控制 结束
    解决思路 1： 应当 从最小正在执行的 方法中 return throw 线程终止异常 到流中捕捉。
    解决思路 2； 一步一步判断 CancellationToken 的状态在进行返回  在监听或等待 其他异步返回时，应当不能继续等待，

    
## 功能 

增删改查：适配 上传/本地
    
异步`逐步：
    硬件的执行方式 逐步走还是异步走 用来 定 模块/模组
    返回耗时 时长/执行结果


## 问题点？？？
1. 怎么 驱动电机？一 “添加模块时选择 对应驱动方式（有统一设置、单独设置）”，二 “由工作流赋值带给里面的模块/流”    
1.1 怎么 驱动电机_运动方式？
2. 异步动作“涉及到 硬件非异步”怎么处理？
2.1. 同步动作 ABC 动作交叉
3. 对于 工作流出现 循环嵌套 怎么处理？




