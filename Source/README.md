
# EdenNetwork Documentation

EdenNetwork의 구조와 메소드의 사용설명을 기술합니다.

## Structure
![Structure of EdenNetwork](https://cdn.discordapp.com/attachments/987651683687481394/1021320379618304011/1.png)

EdenNetwork는 EdenNetServer와 EdenNetClient 두 개의 클래스가 서버와 클라이언트의 역할을 하며 통신한다.
메시지의 송신은 동기/비동기적으로 할 수 있고 수신은 콜백함수를 통해 비동기적으로 일어난다.

![Data Transmission of Eden Network](https://cdn.discordapp.com/attachments/987651683687481394/1021330784327569408/unknown.png)

송신자가 태그와 데이터를 보내면 EdenNetwork Core에서 패킷을 처리하고 수신자는 같은 이름의 태그로 메소드를 등록해 해당 메소드를 통해 데이터를 수신하고 처리할 수 있다.

#### Protocol
![Eden Protocol](https://cdn.discordapp.com/attachments/987651683687481394/1021324354878980106/unknown.png)
 

## Methods

### EdenNetServer Methods

- public EdenNetServer(string ipv4_address, int port, string log_path)
    생성자, 서버를 초기화하고 서버에서 연결을 기다릴 ip주소와 port번호, 로그를 저장할 경로를 입력한다.
    ipv4_adress : 연결을 기다릴 ip 주소
    port : 연결을 기다릴 tcp 포트
    log_path: 서버의 통신 로그를 기록할 파일명

- public void Listen(int max_accept_num, Action<string> DoAfterClientAccept)
    서버에서 listen을 시작하고 클라이언트가 접속할 시 처리할 메소드를 등록한다.
    max_accept_num: 최대 접근 가능 클라이언트 수
    DoAfterClientAccept: 클라이언트가 접속 후 수행될 메소드 params(string: client id)

- public void AddReceiveEvent(string tag, Action<string, EdenData> receive_event)
    수신한 패킷을 처리할 메소드를 등록한다.
    tag: 수신한 패킷의 태그이름
    receive_event: 수신한 패킷을 처리할 메소드 params(string: client id, EdenData: received data)
    example
    ```
    EdenNetServer server = new EdenNetServer(7777);
    server.Listen();
    server.AddReceiveEvent("client_msg", (string client_id, EdenData data) => {
        Console.WriteLine("Client: " + data.Get<string>());
    });
    ```
- public void RemoveReceiveEvent(string tag)
    등록된 ReceiveEvent를 제거한다.
    tag: 등록한 메소드의 태그명

- public void SetClientDisconnectEvent(Action<string> DoAfterClientDisconnect)
    클라이언트가 어떠한 사유로든 연결이 끊어졌을 때 수행되는 메소드를 등록한다.
    DoAfterClientDisconnect: 클라이언트 연결해제 시 수행될 메소드 params(string: client id)

- public void ResetClientDisconnectEvent()
    등록된 ClientDisconnectEvent를 제거한다.

- public void Close()
    서버를 닫는다.

- public bool Send(string tag, string client_id, EdenData data || params object[] data || Dictionary<string, object> data)
    특정 클라이언트에게 데이터를 전송한다. 데이터는 SINGLE, ARRAY, DICTIONARY 형식 중 원하는 형식으로 보낼 수 있다. 반환값은 전송성공 여부이다.
    tag: 전송할 패킷의 태그명
    client_id: 전송할 클라이언트의 id
    data: 전송할 데이터
    ```
    EdenNetServer server = new EdenNetServer(7777);
    server.Listen();
    ...
    //SINGLE
    server.Send("server_msg", client_id, 1);
    //ARRAY
    server.Send("server_msg", client_id, new int[] {1,2,3});
    //DICTIONARY
    server.Send("server_msg", client_id, new Dictionary<string,object>(){
        ["first"] = 1,
        ["second"] = 2
    });
    ```
- public bool Broadcast(string tag, EdenData data)
    모든 클라이언트에게 데이터를 전송한다. 데이터는 SINGLE, ARRAY, DICTIONARY 형식 중 원하는 형식으로 보낼 수 있다.반환값은 전송성공 여부이다.
    tag: 전송할 패킷의 태그명
    data: 전송할 데이터

- public bool BroadcastExcept(string tag, string client_id, EdenData data)
    한 클라이언트를 제외하고 모든 클라이언트에게 데이터를 전송한다. 반환값은 전송성공 여부이다.
    tag: 전송할 패킷의 태그명
    client_id: 전송하지 않을 클라이언트의 id
    data: 전송할 데이터

- public void SendAsync(string tag, string client_id, Action<bool, object> callback, object state, EdenData data)
    Send와 동일한 기능을 비동기적으로 수행한다.
    tag: 전송할 패킷의 태그명
    client_id: 전송할 클라이언트의 id
    callback: 데이터 전송이 완료된 후 실행될 메소드 params(bool: success of sending, object: user data)
    state: callback에 전달할 데이터
    data: 전송할 데이터

### EdenNetClient Methods
- public EdenNetClient(string ipv4_address, int port, string log_path)
    생성자, 클라이언트를 초기화하고 연결할 서버의 ip주소와 port번호, 로그를 저장할 경로를 입력한다.
    ipv4_adress : 연결할 서버의 ip 주소
    port : 연결할 서버의 tcp 포트
    log_path: 통신 로그를 기록할 파일명
- public ConnectionState Connect()
    서버에 연결을 시도한다. 반환값으로 ConnectionState{OK, FULL, NOT_LISTENING, ERROR}를 반환한다.
    OK: 연결성공
    FULL: 서버에 접속된 인원 초과
    NOT_LISTENING: 서버가 켜져있지 않음
    ERROR: 오류발생

- public void AddReceiveEvent(string tag, Action<EdenData> receive_event)
    수신한 패킷을 처리할 메소드를 등록한다.
    tag: 수신한 패킷의 태그이름
    receive_event: 수신한 패킷을 처리할 메소드 params(EdenData: received data)
    example
    ```
    EdenNetClient client = new EdenNetClient("127.0.0.1",7777);
    client.AddReceiveEvent("server_msg", (EdenData data) => {
        Console.WriteLine("Server: " + data.Get<string>());
    });
    ```
- public void RemoveReceiveEvent(string tag)
    등록된 ReceiveEvent를 제거한다.
    tag: 등록한 메소드의 태그명

- public void SetServerDisconnectEvent(Action<string> DoAfterServerDisconnect)
    서버와 어떠한 사유로든 연결이 끊어졌을 때 수행되는 메소드를 등록한다.
    DoAfterServerDisconnect: 서버와 연결해제 시 수행될 메소드

- public void ReSetServerDisconnectEvent()
    등록된 ServerDisconnectEvent를 제거한다.

- public void Close()
    클라이언트를 종료한다.

- public bool Send(string tag, EdenData data || params object[] data || Dictionary<string, object> data)
    서버에게 데이터를 전송한다. 데이터는 SINGLE, ARRAY, DICTIONARY 형식 중 원하는 형식으로 보낼 수 있다. 반환값은 전송성공 여부이다.
    tag: 전송할 패킷의 태그명
    client_id: 전송할 클라이언트의 id
    data: 전송할 데이터
    ```
    EdenNetClient client = new EdenNetClient("127.0.0.1",7777);
    if(client.Connect() != ConnectState.OK) return;
    ...
    //SINGLE
    client.Send("server_msg", 1);
    //ARRAY
    client.Send("server_msg", new int[] {1,2,3});
    //DICTIONARY
    client.Send("server_msg", new Dictionary<string,object>(){
        ["first"] = 1,
        ["second"] = 2
    });
    ```
    
- public void SendAsync(string tag , Action<bool, object> callback, object state, EdenData data)
    Send와 동일한 기능을 비동기적으로 수행한다.
    tag: 전송할 패킷의 태그명
    callback: 데이터 전송이 완료된 후 실행될 메소드 params(bool: success of sending, object: user data)
    state: callback에 전달할 데이터
    data: 전송할 데이터

### EdenData Methods
- public T Get<T>()
    SINGLE형식의 데이터 일 경우 원하는 데이터 타입(T)으로 데이터를 받는다.
- public T Get<T>(ind idx)
    ARRAY형식의 데이터 일 경우 원하는 인덱스(idx)에서 원하는 데이터 타입(T)으로 데이터를 받는다.
- public T Get<T>(string key)
    DICTIONARY형식의 데이터 일 경우 원하는 키(key)에서 원하는 데이터 타입(T)으로 데이터를 받는다.
- examples
    ```
    EdenNetClient client = new EdenNetClient("127.0.0.1",7777);
    client.AddReceiveEvent("server_msg", (EdenData data) => {
        //SINGLE
        Console.WriteLine("Server: " + data.Get<string>());
        //ARRAY
        Console.WriteLine("Server: " + data.Get<string>(1));
        //DICTIONARY
        Console.WriteLine("Server: " + data.Get<string>("key"));
    });
    ```
