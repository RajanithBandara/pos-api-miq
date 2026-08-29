using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Domain.Entities;
using POS.Domain.Interfaces;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.ManageStores)]
public class StoresController : ApiControllerBase
{
    private readonly IRepository<Store, Guid> _storeRepository;
    private readonly IUnitOfWork _unitOfWork;

    public StoresController(IRepository<Store, Guid> storeRepository, IUnitOfWork unitOfWork)
    {
        _storeRepository = storeRepository;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<Store>>), 200)]
    public async Task<IActionResult> GetAllStores(CancellationToken cancellationToken)
    {
        var stores = await _storeRepository.GetAllAsync(cancellationToken);
        return HandleResult(Result<IReadOnlyList<Store>>.Success(stores));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<Store>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetStoreById(Guid id, CancellationToken cancellationToken)
    {
        var store = await _storeRepository.GetByIdAsync(id, cancellationToken);
        if (store == null)
            return HandleResult(Result<Store>.Failure("Store not found.", "NOT_FOUND"));

        return HandleResult(Result<Store>.Success(store));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<Store>), 200)]
    public async Task<IActionResult> CreateStore([FromBody] Store store, CancellationToken cancellationToken)
    {
        var created = await _storeRepository.AddAsync(store, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return HandleResult(Result<Store>.Success(created));
    }
}

[Authorize(Policy = Permissions.ManageStores)]
public class TerminalsController : ApiControllerBase
{
    private readonly IRepository<PosTerminal, Guid> _terminalRepository;
    private readonly IUnitOfWork _unitOfWork;

    public TerminalsController(IRepository<PosTerminal, Guid> terminalRepository, IUnitOfWork unitOfWork)
    {
        _terminalRepository = terminalRepository;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PosTerminal>>), 200)]
    public async Task<IActionResult> GetAllTerminals(CancellationToken cancellationToken)
    {
        var terminals = await _terminalRepository.GetAllAsync(cancellationToken);
        return HandleResult(Result<IReadOnlyList<PosTerminal>>.Success(terminals));
    }

    [HttpGet("store/{storeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PosTerminal>>), 200)]
    public async Task<IActionResult> GetTerminalsByStore(Guid storeId, CancellationToken cancellationToken)
    {
        var terminals = await _terminalRepository.FindAsync(t => t.StoreId == storeId, cancellationToken);
        return HandleResult(Result<IReadOnlyList<PosTerminal>>.Success(terminals));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PosTerminal>), 200)]
    public async Task<IActionResult> RegisterTerminal([FromBody] PosTerminal terminal, CancellationToken cancellationToken)
    {
        var created = await _terminalRepository.AddAsync(terminal, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return HandleResult(Result<PosTerminal>.Success(created));
    }
}
