using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Customers.DTOs;
using POS.Application.Customers.Services;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.ManageCustomers)]
public class CustomersController : ApiControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerDto request, CancellationToken cancellationToken)
    {
        var result = await _customerService.CreateCustomerAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetCustomerById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _customerService.GetCustomerByIdAsync(id, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CustomerDto>>), 200)]
    public async Task<IActionResult> SearchCustomers([FromQuery] string query, [FromQuery] Guid? storeId, CancellationToken cancellationToken)
    {
        var result = await _customerService.SearchCustomersAsync(query, storeId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CustomerDto>>), 200)]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] Guid? storeId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _customerService.GetCustomersPagedAsync(storeId, pageNumber, pageSize, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDto>), 200)]
    public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateCustomerDto request, CancellationToken cancellationToken)
    {
        var result = await _customerService.UpdateCustomerAsync(id, request, cancellationToken);
        return HandleResult(result);
    }
}
